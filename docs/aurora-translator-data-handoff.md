# AuroraTranslator data and importer handoff

**Decision baseline: September 13, 2026.** Audience: AuroraTranslator maintainers
and the future shared importer library. This document consolidates the accepted
direction and the current Aurora Lights implementation. It is intended to travel
with a handoff; the essential rules are included here rather than only linked.

**Status:** Aurora Lights checkpoint `624b6b7cec82e8ad6d0efffb36ba414908469a15`
(`Add protected local corrections and content review foundations`) records this implementation on `main`.
The Lights working tree is clean. Nothing was pushed or released; no version bump or
production database refresh was performed. Canonical uniqueness, safe option consolidation,
verified-download automatic acceptance and shared-library extraction remain follow-up work.
The bundled Translator executable itself has not gained correction handling.
This handoff was initially moved into AuroraTranslator as an uncommitted file;
the standalone implementation and pre-commit review are recorded in section 11.

**Translator source update, September 13, 2026:** the standalone `sqlite-import`
route now uses the bounded preparation/candidate workflow described in section
11. This supersedes the earlier lack of standalone lifecycle support for this
checkout's source, not for the previously bundled executable. No binary was
published or copied into Lights, and no installed content or production database
was changed.

**Location:** AuroraTranslator checkout (`5eApiTranslator/docs/aurora-translator-data-handoff.md`). Supporting links point to the sibling `Aurora-Lights` checkout.

**Maintenance rule:** keep this handoff current as the session's features are
completed. Update accepted rules, implementation boundaries, tests, and actual
deployment status together; retain historical evidence as dated observations.
Latest content deployment: the six known hotfixes now carry embedded v1 metadata
(ten review-pending operations). See sections 7–9 and the
[annotation record](../../Aurora-Lights/docs/hotfix-metadata-annotation-2026-09-13.md).

Latest architecture clarification: conflict resolution belongs in shared
**content preparation**, orchestrated by the app or headless CLI, before the
SQLite writer receives finalized data. A neutral typed review/provenance foundation
and read-only analyzer now exist in `Builder.Data/Content/Review`; they are not
wired into production imports/UI. See [the contract and XMLHelper reuse findings](../../Aurora-Lights/docs/content-preparation-contract.md).

## 1. Decisions to preserve

| Topic | Accepted rule | Superseded interpretation |
| --- | --- | --- |
| Canonical identity | One nonempty `aurora_id` identifies one canonical element across the central elements table; its numeric `element_id` maps one-to-one. Overrides are a separate layer. | `(aurora_id, source)` as the permanent key; separate definitions just because source labels differ. |
| Harmless repetition | Silently consolidate demonstrably identical declarations of the same ID. Retain every supplying file's provenance. | Keep redundant canonical rows, or discard provenance with the duplicate. |
| Conflicting definitions | Establish an authoritative revision relationship or require explicit resolution. | Choose by insertion order, file timestamps, arbitrary package precedence, or `INSERT OR IGNORE`. |
| Choice equivalence | Preserve mechanically and referentially distinct choices. Equal names/descriptions are insufficient. | Flatten options by display text and keep an arbitrary ID. |
| Local content | Explicit user/local content is authoritative in the builder and may be imported/cached, provided it remains recognizable as an override and the base is preserved. | Local content must never enter SQLite, or may become an indistinguishable competing base row. |
| Correction intent | Keep versioned intent in the local XML and mirror it in SQLite. XML survives a database rebuild. | Require a new sidecar file, or store the only copy of intent in the database. |
| Upstream updates | Only marked corrections stay pinned; baseline-confirmed, unchanged companion definitions follow upstream. | Freeze every definition in a full-file hotfix forever. |
| Incorporated fixes | Automatically accept a complete correction/group matched by verified content fetched from its authoritative upstream URL, after successful import. Disk-only matches and partial/differing repairs still require review. | Both blanket manual approval for every published exact match and automatic approval based merely on an installed-file match. |
| File retirement | After corrections are relinquished and the entire effective file is redundant, retire it automatically after a successful import. Preserve unique local content. | Delete a mixed file merely because one corrected element matches upstream. |
| Import ownership | Extract AuroraTranslator's authoritative importer into a versioned library shared by the CLI and Lights, including database maintenance. | Maintain a separately evolving copied importer in Lights. |
| Platform loading | SQLite must remain a viable normal path on supported platforms, including Android. Runtime XML is recovery. | Make runtime XML the only practical non-Windows option. |

These are the latest decisions. Earlier audit documents contain intermediate
proposals and historical counts; use this handoff when those conflict.

## 2. Identity, provenance, and references

### Terminology

- **Aurora ID:** the XML `element/@id`, stored as `aurora_id`; the content identity
  used by grants, appends, selections, and character references.
- **Numeric element ID:** an internal SQLite key. Do not persist a database row
  number as a durable character identity across database rebuilds.
- **Source:** a publication/source represented by Aurora's Source elements and
  source labels. It supplies attribution and availability/filter information.
- **Package:** a grouping/distribution of sources, such as official, third-party,
  or homebrew content. Existing `content_packages` is an implementation concept;
  it must not silently define what the user means by Source.
- **Provenance:** the supplying root/repository, file, declaration, and known
  revision. This identifies where a definition or correction came from.
- **Selected row:** existing row-aware choice keys identify a prompt/selection
  slot. They do not choose between competing versions of an element definition.

The schema currently has a central `elements` table, with subtype data referring
to its numeric key. The target is **global canonical Aurora-ID uniqueness**, not
independent uniqueness per subtype or source. This constraint is not yet enforced.

An identical declaration in two files should produce one canonical element with
two provenance associations. Deleting or disabling one supplier must not erase
an element still supplied by another eligible file. A migration must replace the
current assumption that deleting a source file can cascade-delete all of its
elements. It must also preserve dependent foreign keys, source associations,
deferred grants/selects, support links, caches, and availability settings.

There is no need for a table of redundant gameplay definitions merely to record
identical copies. A many-to-one declaration/file provenance relation is still
needed. Unresolved conflict evidence is different from a duplicate canonical row.

Compare complete definitions before consolidating same-ID copies: type, source
information, description, requirements, rules, grants, setters, supports,
exclusions, spellcasting, sheet data, operational children, and relevant ordering.
Do not treat a changed source attribute as automatically harmless: the Book of
Beasts case was explicitly investigated and corrected as an authoring error.

Different IDs should remain distinct unless an explicit canonical alias/migration
preserves incoming references and saved choices. Matching text, or even matching
outgoing mechanics, does not establish that incoming references are interchangeable.
Do not guess which historical choice a previously ambiguous ID represented.

**Pending:** `BuildSelectionOptionResolver.DeduplicateOptions` still groups by
name and description and combines source labels. Remove that unsafe consolidation
or replace it with proven canonical equivalence; do not describe it as fixed.
Define the exact ID case/whitespace policy before the uniqueness migration. No
new case-folding rule was agreed; avoid introducing silent normalization as part
of this handoff.

## 3. Embedded XML correction contract, version 1

The implementation is `Builder.Data/Files/LocalCorrectionDocument.cs`. Reuse this
contract and its fixtures during extraction; do not independently approximate it.

Place one namespaced `corrections` section directly under the existing
**unnamespaced** `elements` root, beside `info`, `element`, and `append`. Keep the
namespace declaration on that section. Preserve existing valid `info/update` data.
Do not place correction metadata inside gameplay elements or create a metadata-only
`info` block. The prefix `al` is illustrative; the namespace URI is significant.

```xml
<!-- Schematic: generate the full baseline and fingerprints using the API. -->
<al:corrections xmlns:al="urn:aurora-lights:corrections:1"
                version="1" source-path="core/example.xml">
  <al:baseline encoding="escaped-xml">...escaped complete original elements document...</al:baseline>
  <al:correction key="example-rename" operation="rename"
                 target-id="ID_OLD" replacement-id="ID_NEW"
                 original-fingerprint="...original declaration fingerprint..."
                 state="review-pending" group="example-and-parent-grants">
    <al:reason>Separate the incorrectly reused identity and repair its grants.</al:reason>
  </al:correction>
</al:corrections>
```

The replacement gameplay definition remains an ordinary `element` in the local
file. The baseline is the **complete original authoritative file**, stored as
escaped XML text, not live descendants. This preserves the original duplicate
declarations and lets unchanged companions be identified. Recursive compatibility
repair passes were observed to mutate live XML inside metadata; escaped text
avoids that problem.

| Field | Contract |
| --- | --- |
| Namespace / version | `urn:aurora-lights:corrections:1` / `1`. Unknown or ambiguous metadata fails conservatively. |
| `source-path` | Authoritative XML path relative to the same content root, outside all of `user`; no rooted path, escape, or symlink/junction traversal. |
| `baseline` | Exactly one `encoding="escaped-xml"` text payload containing a valid unnamespaced `elements` document. |
| `key` | Nonempty correction identifier, unique within the file. Mirrored identity is `(file_path, correction_key)`. |
| `operation` | `replace`, `rename`, `remove`, or `add`. |
| `target-id` | Original/target Aurora ID; for `add`, the new local ID. |
| `replacement-id` | Required for `rename`; must differ from the target ID. |
| `original-fingerprint` | Selects the intended original declaration when malformed input reuses an ID. Required in practice when ID alone is ambiguous. |
| `state` | `review-pending` pins; `accepted-upstream` explicitly relinquishes the operation. |
| `group` | Optional related-repair group, including parent grants. Partial group acceptance is rejected within files and at sync across files. |
| `reason` | Optional human-readable explanation; can include upstream issue context. |

Metadata is active only under `user/local`. Existing unmarked files are not
automatically annotated or classified. Multiple managed files targeting the same
authoritative file are rejected rather than stacked by an inferred priority.

### Fingerprints and authority

`Fingerprint(XElement)` hashes UTF-8 System.Text.Json serialization of a structural
array: expanded element name, non-namespace attributes sorted ordinally by expanded
name, and ordered child elements/text. SHA-256 is uppercase hexadecimal. Text,
including whitespace, is preserved; comments and processing instructions are
excluded. This is a conservative comparison, **not semantic equivalence**. Keep
golden fixtures when moving runtimes/serializers. V1 has no separate fingerprint
algorithm attribute; a changed algorithm needs an explicit compatible migration.
`FileFingerprint` separately hashes original file bytes for stale-review checks.

Current origin validation compares baseline and upstream update URLs exactly and
rejects an upstream version older than the baseline when both versions parse as
`System.Version`. It does not establish repository authority cryptographically,
compare unrelated version sequences, or detect every rollback after a later
accepted revision. Full repository/revision provenance remains future work. File
mtime and source display order are not substitutes for authority.

## 4. Update, protection, and retirement behavior

Evaluation starts from the current authoritative file, then applies protected
operations. A rename/removal must suppress the intended obsolete declaration;
ID-only upsert is insufficient. When two malformed declarations share an ID, a
fingerprint identifies the one being repaired without deleting the other.

| Situation | Required outcome |
| --- | --- |
| Marked replacement; upstream changes target | Preserve the local correction, retain the new upstream evidence, require review. |
| Installed content matches a marked correction without verified download evidence | Report incorporated/review required; keep it pinned. |
| Verified authoritative download incorporates a complete correction and its linked repairs | Automatically accept after validating/importing those exact bytes. This new policy is not implemented yet. |
| Baseline-confirmed unchanged companion changes/disappears upstream | Follow the authoritative update/removal. |
| Local edit/addition/removal is not explained by correction metadata | Preserve its effect and flag classification/review; do not assume it is disposable. |
| Unmarked local content now exactly matches upstream | Treat it as redundant when evaluating retirement. |
| Upstream claims an explicit local addition with different content, or a rename destination is occupied | Reject the ambiguous refresh; do not silently overwrite either side. |
| Local append/other operational content changes | Preserve conservatively as a collection and require review unless it matches upstream. |
| Local root attributes change | Preserve their effects and include them in review/retirement eligibility. |
| Local root has `ignore="true"` | Do not apply corrections or auto-retire the disabled file. |
| Metadata/version/origin is invalid or ambiguous | Preserve evidence and reject managed processing; do not silently load the uncorrected base as recovery. |
| Every operation is accepted and the complete effective content matches upstream | Retire after successful database activation if no unique/unclassified local effects remain. |
| Local file is deliberately removed | Remove active mirror state, restore base behavior, and invalidate affected caches. |

`AcceptUpstream` is an explicit review API. The caller supplies the local and
upstream file hashes actually reviewed and the chosen correction keys. It rejects
stale inputs, validates the resulting evaluation/group state, and writes XML
atomically. Acceptance means deliberately relinquishing that correction; it is
not an automatic equality-based action. Cross-file group validation exists at
sync, but a transactional multi-file review editor is not implemented.

**Latest policy refinement:** automatic acceptance is allowed for an exact
published match observed while fetching the configured authoritative upstream
URL (for example, its raw GitHub URL). The current `AcceptUpstream` implementation
does not yet provide that path. Carry fetched-byte hashes, origin evidence, and
the evaluated local correction hashes through staging/import; do not infer
publication from already-installed bytes or self-declared XML URLs. A bare 304
is not proof unless prior verified download evidence binds the representation
to the same bytes. Compare the full repair, including renamed IDs, removals,
source attribution and every linked parent-grant operation. Partial groups or
different outcomes remain pinned. Successful import and unchanged inputs must
precede committed acceptance; retirement still requires whole-file redundancy.
Persist acceptance in XML and mirror its publication evidence in SQLite, without
a sidecar. Recovery across XML/database writes must preserve protection on failure.
See [the refined policy](../../Aurora-Lights/docs/local-correction-policy.md). No existing hotfix was
automatically accepted by recording this policy.

Automatic index updates refuse to overwrite existing `user/local` XML or existing
files carrying correction metadata. Generic `ElementsFile.SaveContent` likewise
protects existing metadata; explicit review uses the dedicated writer. Missing
markers in legacy local edits are not permission to overwrite/delete them.

Retirement renames `file.xml` to `file.xml.retired-<unique-id>`. The original is
recoverable and no longer matches `*.xml` scans; no new sidecar is required.
Retirement happens **after** the validated database is active. A locked file can
remain redundant until a later sync. Retired tracking rows remain historical;
their presence must not reactivate an override. An XML-only rebuild reconstructs
active intent, not a guaranteed complete history of previously retired files.

## 5. SQLite mirroring and activation

`Aurora.Importer/LocalCorrectionSync.cs` currently wraps the Lights import routes.
It stages effective XML into temporary roots: the authoritative staged file gets
effective gameplay, while the managed local staged file retains bookkeeping but
no second set of gameplay declarations. Installed upstream/local XML is untouched
during staging. Both existing importers can consume this prepared input.

**Current storage is indexed effective elements plus separately preserved XML
evidence.** It is not yet a fully normalized canonical-base/override relational
schema. Do not claim the current `elements` rows are always pristine upstream.

| Table | Current columns / purpose |
| --- | --- |
| `local_override_files` | `file_path` primary key; `source_path`, `local_xml`, `baseline_xml`, `upstream_xml`, `effective_xml`, `status`, `review_details`, `suppressed_ids`. Review details and suppressed IDs are JSON arrays. |
| `local_corrections` | Primary key `(file_path, correction_key)`; `operation`, `target_id`, `replacement_id`, `original_fingerprint`, `state`, `review_group`, `reason`. File foreign key cascades on deletion. |
| `local_correction_inputs` | `path` primary key, `sha256`; hashes of actual installed XML inputs, separate from staged importer hashes. |

File statuses are currently `review-required`, `ready-to-retire`, and `retired`.
Incorporation is represented in review details, not a separate operation state.
Local file/input paths are machine-local absolute paths; origin `source_path` is
relative. Do not mistake these tables for portable repository identity, and do
not publish personal local overrides or their mirrored XML in a distributable
upstream snapshot by blindly copying a user's working database.

For managed corrections or mirror cleanup, the wrapper backs up the current DB to
a candidate, preserving availability settings, imports staged inputs, checks
SQLite integrity/foreign keys and corrected IDs in `resolved_elements_cache`,
mirrors evidence, and checks the input file set/hashes again. It activates only
after successful validation. Failed/raced refreshes preserve the working DB.
Package toggles share the app's sync lock. Failed refreshes stop content reload
before character tabs/loaded elements are replaced.

Runtime overlay readers use mirrored effective XML only when both local and
authoritative hashes still match. Otherwise they evaluate current XML. Apply
suppressed IDs and effective append content, and invalidate DB/runtime caches when
content changes. Removing a local override must not leave a corrected or obsolete
row alive through an old cache. The raw runtime XML paths use the same evaluator.

Runtime suppression is now scoped to the authoritative file's provenance, not
only the old Aurora ID. `ElementBase.ContentFilePath` is populated by DB/XML
loaders; cached correction content also returns the resolved origin path. A DMG
Staff rename must not remove the legitimate XGTE Staff that retains the old ID.
Unknown runtime provenance is not permission to remove another definition. This
runtime field is not a durable character identifier or the pending normalized
database provenance schema.

Commit-readiness review also closed the raw overlay error-handling gap: managed
file/element failures must propagate rather than be logged as skipped content in
an otherwise successful load. Unmarked legacy files retain their existing skip
behavior. This does not relax correction protection when metadata is unsupported.

**Limit:** when no managed files or previous mirror records exist, the wrapper
delegates to the original import path. General candidate activation and global
collision rejection for every unmanaged import are not yet established by this
implementation. Corrected-ID presence is also not proof of all desired reference
semantics; retain the focused grant/equivalence tests during migration.

## 6. Snapshot compatibility and shared importer architecture

### Current compatibility fix

Lights now accepts **schema 1, data 10 or 11**. The copied importer still writes
data 10. Translator data 11 spellcasting is reconstructed from complete `raw_xml`,
preserving child order, expressions, `known`, and `all` through the runtime parser.
Missing/malformed spellcasting XML or a load that skips elements is a failure,
not a successful partial database. The XML reader prohibits DTDs/external entity
resolution. Do not label the copied writer v11 without implementing that format.

Ordinary stale checking now normalizes stored path separators: actual Windows
Translator snapshots contain backslashes while another catalog path uses slashes.
This matters after the final managed override disappears and ordinary checking
resumes.

The separate **reader compatibility contract** is still planned. The goal is that
publishing a new compatible data snapshot does not require editing Lights for
each writer/snapshot version. Library package version, schema/migration version,
reader capabilities, and content publication revision serve different purposes.
The exact future manifest/capability fields have not been implemented or agreed
here; do not infer unrestricted forward compatibility from the v10/v11 fix.

### Current routes and intended ownership

| Route | Today | Migration requirement |
| --- | --- | --- |
| Windows, one content root, bundled executable available | `ContentDatabaseService` invokes `sqlite-import` through the correction wrapper. | Keep CLI behavior aligned with the shared library. |
| Other platforms, multiple roots, or unavailable bundled tool | Copied `AuroraContentImporter`/`AuroraSqliteImporter`, also wrapped for corrections. | Replace copied writer with the same platform-capable library. |
| Package activation | Copied cache/deferred-link rebuilding remains. | Migrate maintenance alongside full import; separate availability/display ordering from identity resolution. |
| Direct standalone Translator CLI | Existing importer, without Lights' lifecycle wrapper. | Add the shared lifecycle contract before claiming standalone parity. |
| Runtime XML ingestion | Recovery and user overlay behavior. | Retain recovery without bypassing correction protection. |

A bundled-process failure returns a failed sync; it does not invoke the copied
writer as a second attempt. A single-root CLI must not be called sequentially for
each root against one DB: absent-file cleanup can remove another root's content.
Preserve multi-root semantics in the shared API.

Recommended dependency direction: a platform-neutral importer/contract library
owns parsing, identity/provenance, correction evaluation, migrations, import,
cache maintenance, and validation. Translator CLI and Lights call it. App UI,
dialogs, platform paths/permissions, progress display, and review presentation
remain in the host. Preserve cancellation, progress reporting, incremental work,
and recoverable failures. Current `Aurora.Importer` references `Builder.Data` for
the XML contract; extraction must deliberately relocate/share that dependency
rather than introduce a dependency on the Lights application.

Within that shared library, distinguish preparation from writing. Preparation
owns conflict classification, correction/evidence evaluation and review results;
the app or authoring tool supplies interaction. The writer validates and stores
the finalized content/provenance, rejecting invalid input without choosing winners
or proposing content repairs. Translator CLI should use the same preparation
rules headlessly and report unresolved cases. Existing `LocalCorrectionSync` is
a workflow wrapper despite its present project location; its current placement
does not require putting resolution policy inside the future SQLite writer.

AuroraXMLHelper already supplies diagnostic/repair records and preview rendering.
Reuse/adapt its provider JSON and fixtures rather than invent a second authoring
rule vocabulary. Its manual duplicate-ID regeneration suggestion must not override
our identical-copy consolidation policy or automatically rewrite linked IDs.

Validate Android import **and subsequent SQLite loading** early using the shared
in-process library and appropriate SQLite/platform packaging. The current Windows
executable route provides no Android proof. Desktop platform-specific CLI builds
can remain optional wrappers; do not rely on running that desktop executable on
Android. No Android/shared-library implementation was verified in this work.

## 7. Content cleanup ledger to carry forward

These are recorded local cleanup results, not newly published upstream fixes.
Primary installed content root:
`C:\Users\Ralla\Documents\5e Character Builder\custom`.

The final recorded nonlocal XML scan contained **20,767 declarations, 20,762
distinct IDs, five identical repeats, zero differing repeats, and zero parse
errors**. That bounded scan supports canonical ID uniqueness; it does not certify
future downloads, every additional content root, or the older production DB.

The large earlier apparent source split was **1,501 paired creature IDs** in stale
Book of Xellarant and current Book of Beasts copies. The differences were source
labels after formatting normalization. **The Book of Beasts is authoritative for
these creatures.** This was not evidence that source should be part of identity.

### Archived duplicates

Archive: `C:\Users\Ralla\Documents\Aurora Content Archive\2026-09-13-duplicate-cleanup`.
Files and original local index were retained with hashes/manifest.

| Archived path relative to custom | Retained counterpart / rationale |
| --- | --- |
| `supplements/the-book-of-xellarant/creatures.xml` | `the-book-of-xellarant/creatures.xml`, Book of Beasts. |
| `unearthed-arcana/20200204.xml`, version 0.0.2 | `20200206.xml`, version 0.0.5. |
| `unearthed-arcana/20170117.xml`, version 0.0.5 | `20170116.xml`, version 0.0.6. Filename date alone did not determine newness. |
| `the-book-of-xellarant/class-ranger-revised.xml` | Official PHB Ranger and separate Tasha optional features; the entire custom file was archived. |
| `the-book-of-xellarant/ranger-drakewarden.xml` | Official Fizban Drakewarden. |

The archived Revised Ranger had 66 declarations, including 42 unique IDs. Those
custom definitions are inactive/recoverable, not silently remapped to PHB. Tasha
optional replacement features remain separate. Treat UA as a revision/beta
channel for demonstrated collisions; this is not permission to collapse every
UA definition with a similarly named official element.

The creature URL check recorded a stale `main/creatures.xml` URL returning 404
and `master/the-book-of-xellarant/creatures.xml` returning 200. Repository commit
`3e208c56d3ead35a27cb1dec35be0f630145e128` (2026-04-27) added the current Beasts
source. These are historical observations, not a promise that those endpoints
remain live. The upstream index still listed the two archived Ranger files;
refreshing it can restore them until upstream is corrected.

### ID repairs: seven conflicts, six full files

| File relative to custom | Accepted repair |
| --- | --- |
| `core/dungeon-masters-guide-2024/items-staffs.xml` | DMG Staff of Flowers becomes `ID_WOTC_DMG24_MAGIC_ITEM_STAFF_OF_FLOWERS` with DMG 2024 source attribution. XGTE identity/references remain intact. |
| `core/dungeon-masters-guide-2024/items-weapons.xml` | Walloping becomes `ID_WOTC_DMG24_MAGIC_ITEM_AMMUNITION_WALLOPING`; retain Adamantine's original ID. |
| `reddit/reddit-unearthed-arcana/arcane-artillery/guns.xml` | Keep single-piece `ID_RDDT_AA_MUSKETBALL` (1 sp, 0.05 lb); remove the 20-piece duplicate. Quantity handles multiples. |
| `reddit/reddit-unearthed-arcana/blazing-dawn-players-companion/fighter-devout.xml` | Use `ID_JONOMAN3000_ARCHETYPE_FEATURE_DEVOUT_ZEALOTS_DEVOTION_DEFENDER_OF_KIN` and `ID_JONOMAN3000_ARCHETYPE_FEATURE_DEVOUT_ZEALOTS_DEVOTION_SLAYER_OF_FOES`; repair the parent's two empty level-3 grants. |
| `ryokos-guide-to-the-yokai-realms/race-tatsumi.xml` | Keep Cloudstep's ID; Heartening Breath becomes `ID_RGTTYR_RACIAL_TRAIT_TATSUMI_RYUJIN_HEARTENING_BREATH`; change the parent's second Cloudstep grant to Heartening Breath. |
| `third-party/genuine-fantasy-press/forgotten-secrets/items.xml` | Keep Cimbalom; use `ID_GFP_COFSA_MAGIC_ITEM_WONDROUS_ITEM_KABA_GAIDA_OF_NEVIHTA`. Ancient Heart +3 becomes `ID_GFP_COFSA_MAGIC_ITEM_WONDROUS_ITEM_ANCIENT_HEART_STRENGTH_THREE`; +2 retains TWO. |

The six fixed files were copied byte-for-byte to
`custom\user\local\id-hotfixes-20260913\<original relative path>`. Originals,
hashes, manifest, and patch are in
`C:\Users\Ralla\Documents\Aurora Content Archive\2026-09-13-id-hotfixes-originals`.
Use those originals as baselines when explicitly annotating these repairs.
**The six installed hotfixes are now annotated:** seven renames, one removal,
and two parent-definition replacements. All ten operations are `review-pending`;
Devout's three related operations and Tatsumi's two share their respective review
groups. Embedded baselines are the hash-verified archived originals. The installed
nonlocal files were already locally corrected, so an "incorporated" comparison
does not prove the upstream author published the fix. No operation was accepted
or retired. Pre-annotation local copies are recoverable in
`C:\Users\Ralla\Documents\Aurora Content Archive\2026-09-13-metadata-annotation`.
No balance/prose corrections or ambiguous saved-character migrations were made.
Production SQLite has not been refreshed; its mirror will populate on a successful
sync using the updated Lights code. Other legacy local files remain unclassified.

The five remaining identical repeats are Position of Privilege, Safe Haven,
Supreme Sneak: Stealth Attack Feature Replacement, Aspect of Ashura, and Crown of
Disgust. Position of Privilege and Safe Haven include cross-file suppliers;
preserving provenance matters even after the duplicate row disappears.

No upstream content PR was submitted. A prior reminder already exists; no new
reminder is needed merely to consume this handoff. Known destinations are
AuroraLegacy/elements (DMG), community-elements/elements-reddit (guns/Devout), and
StrangeMeta/homebrew (Tatsumi). Confirm the maintained Forgotten Secrets successor
before submitting its repair.

## 8. Verification to retain and extend

Latest commit-readiness check: **63 focused tests passed together** across snapshot
compatibility, correction metadata/lifecycle/provenance, index updates and the
review contract. The Windows app build passed with zero warnings/errors. The two
runtime-failure regressions were rerun successfully after tightening malformed
managed-versus-legacy file handling. `git diff --check` passed. Temporary databases
and logs are ignored; the installed XML/archives outside this repository are not
included by a Lights code commit. At that time, no files had been staged or committed by this
readiness check. Advanced UI, canonical uniqueness, verified-download automatic
acceptance and production integration of the review model remain pending work.

The following are the recorded results from the implementation work, not tests
rerun for this documentation handoff. They used temporary data; production XML
was not annotated and the production DB was not refreshed.

| Evidence | Recorded result |
| --- | --- |
| `LocalCorrectionLifecycleTests` + `CorrectionMetadataCompatibilityTests` + `ContentIndexUpdateServiceTests` | 36 passed: 19 lifecycle, 12 metadata compatibility, 5 updater. |
| Lifecycle suite with actual bundled Translator selected by `AURORA_TEST_TRANSLATOR` | 19 passed through the lifecycle wrapper. This does not prove standalone CLI lifecycle support. |
| Windows app build | Passed with zero errors. |
| `TranslatorSpellcastingReaderTests` | Earlier focused compatibility run: 7 passed. |
| Six corrected files imported with bundled Translator | 247 elements / 247 unique IDs; three repaired grants resolve. |
| Metadata-only full-file comparison through bundled Translator | 247 elements, 56 compared tables unchanged, integrity/reference checks passed. |
| Aurora XMLHelper shape check | Six annotated test copies, zero errors/warnings. |

Subsequent annotation verification: six full hotfix files evaluated against both
unfixed originals and current installed sources with unchanged effective gameplay
and no unclassified edits. A temporary import through the actual bundled Translator
and wrapper produced **248 unique elements** (247 repaired-file elements plus the
legitimate XGTE Staff), six mirrored files, ten pinned operations, and three
resolved parent grants. The 19 lifecycle plus two new provenance tests passed;
the Windows app build passed with zero warnings/errors. This is separate from
the earlier 36-test run, not a claim that that entire set was rerun.

Existing lifecycle coverage includes pinning, companion updates/removals,
incorporated-but-unreviewed corrections, explicit/grouped/stale reviews, duplicate
renames/removals, unclassified edits, root/operational effects, invalid metadata
and origins, mirror reconstruction/cache invalidation, retirement, local deletion,
index protection, and failed/raced imports.

Compatibility limits: the documentation did not promise an extension mechanism.
The current tested parsers tolerate the chosen file-level section. Actual Aurora
Legacy execution and Constellations/Aurora Studio editor round trips were not
verified; neither was every XMLHelper save/export workflow. Do not advertise
universal editor compatibility. Preserve unknown metadata on round trips or
explicitly reject destructive transformations.

Recommended acceptance cases for Translator/library migration, beyond preserving
the existing suite:

1. Same-ID identical declarations in one/multiple files produce one canonical row;
   removal/update/disable of one supplier preserves other valid suppliers.
2. Different definitions of one ID reject activation with provenance-rich conflict
   details. Verified revisions and explicit corrections remain distinguishable.
3. Canonical migration preserves foreign keys, availability, grants/appends/supports,
   and saved Aurora IDs; it does not guess at ambiguous historical repairs.
4. Equal names/descriptions with different mechanics or incoming references remain
   separate choices through selection/save/load.
5. CLI and library produce equivalent effective data and correction decisions for
   the same roots/options, including multi-root cleanup and package maintenance.
6. Snapshot publication proves reader compatibility and excludes personal override
   state; future unsupported contracts fail clearly rather than partially load.
7. Android and desktop library import/load succeed without requiring whole-corpus
   runtime XML; cancellation/failure preserves a usable previous database.
8. A complete repair fetched from the authoritative URL is automatically accepted
   only after successful import; disk-only matches, unverified 304 responses,
   incomplete linked groups, changed local inputs, and failed imports stay protected.

## 9. Recommended implementation order and remaining boundaries

1. **Carry this contract and fixtures into Translator first.** The bounded
   standalone source port in section 11 now implements this initial step. Share the XML
   contract/evaluator rather than inventing another metadata format. Make direct
   CLI use honor lifecycle behavior, not merely ignore the metadata section.
2. **Implement canonical identity/provenance transactionally.** Classify duplicate
   declarations, preserve multiple suppliers, migrate old rows/dependents, then
   enforce nonempty unique canonical IDs. Keep overrides distinct and preserve
   source/availability behavior. An index-only patch is insufficient.
3. **Fix option identity end to end.** Remove text-only collapse and verify
   selections, grants, and saves. Retire arbitrary winner ranking only after
   canonicalization and override resolution replace its actual responsibilities.
4. **Known hotfix annotation completed.** Preserve the ten explicit operations and
   related grants. Production sync and verification of an upstream publication
   remain separate steps; do not infer intent for other existing local files.
5. **Extract and version the shared importer.** Migrate all creation/refresh/cache
   mutation callers, including developer tools. Keep read/health/recovery helpers
   still needed by the app. Validate Android early in this extraction.
6. **Add reader-contract publication checks and the compact advanced review UI.**
   Settings currently exposes statuses/reasons, not a complete three-way editor.
   Surface small sync conflicts in the builder; reuse Constellations/XMLHelper/
   Studio logic where practical. Protection must work with Advanced options off.

The remaining implementation details include ID normalization, durable multi-root
repository/declaration identity, full revision authority/rollback tracking,
multi-file atomic review, and the library/reader version handshake. These have
not been silently settled by the current prototype. Use the accepted invariants
above to resolve them; avoid reopening the superseded composite-key or sidecar
proposals without an actual new requirement.

Planning estimates for one familiar developer: **40–72 hours** for the complete
bounded advanced diff/resolution UI and **64–112 hours** for canonical uniqueness
across active writer/reader paths. Combined: **104–184 hours**, roughly 13–23
eight-hour workdays. These include focused verification; shared-library extraction,
Android validation, and upstream PR turnaround are excluded. See
[the scope and estimate breakdown](../../Aurora-Lights/docs/content-migration-estimates.md). They are
planning ranges, not promises about elapsed agent execution time.

The small review/provenance foundation is now implemented and verified by 16
focused tests. It records stable-root/file/revision/declaration identities,
definition snapshots, review cases, explicit reference-inspection completeness,
correction review inputs, publication-evidence shape and proposed workflow intent.
It does not execute resolutions or capture/verify publication evidence yet. Two
read-only XMLHelper analyzer probes confirmed existing repair vocabulary and the
need to adapt identical-duplicate handling. Persistent root registration,
reference scans, production adapters, transactional migration and the UI remain
pending; the foundation does not complete the full design/implementation estimate.

## 10. Code and evidence map

Paths below are relative to **Aurora-Lights**, not AuroraTranslator. Copy the
relevant files/fixtures or port their behavior deliberately; merely copying this
document does not install the working-tree implementation.

| Responsibility | Entry points |
| --- | --- |
| XML contract, evaluation, explicit review | `Builder.Data/Files/LocalCorrectionDocument.cs` |
| Mirror, staged import, activation, retirement | `Aurora.Importer/LocalCorrectionSync.cs` |
| Import and package maintenance | `Aurora.Importer/AuroraContentImporter.cs`, `Aurora.Importer/AuroraSqliteImporter.cs` |
| App sync/reload | `Aurora.App/Services/ContentDatabaseService.cs`, `Aurora.App/Services/ContentService.cs` |
| DB compatibility/reconstruction | `Aurora.App/Services/DbElementLoader.cs`, `Aurora.Importer/TranslatorSpellcastingReader.cs` |
| Runtime managed overlays/recovery | `Aurora.App/Services/RawUserXmlOverlayService.cs`, `Aurora.App/Services/XmlContentFallbackService.cs`, `Aurora.Logic/Services/Data/DataManager.cs` |
| Automatic overwrite protection | `Aurora.Logic/Services/Content/ContentIndexUpdateService.cs`, `Builder.Data/Files/ElementsFile.cs` |
| Choice identity and pending collapse fix | `Aurora.Logic/Services/BuildSelectionOptionResolver.cs`, `SelectionRuleIdentityService.cs`, `SelectionRuleRegistrationService.cs` in the same directory |
| Current status UI | `Aurora.App/Components/Pages/Settings.razor` |
| Focused tests | `Aurora.Tests/Tests/LocalCorrectionLifecycleTests.cs`, `CorrectionMetadataCompatibilityTests.cs`, `TranslatorSpellcastingReaderTests.cs`, `ContentIndexUpdateServiceTests.cs` in the same directory |
| Fixtures / CLI metadata probe | `Aurora.Tests/Fixtures/CorrectionMetadata/`, `tools/test-correction-metadata.py` |
| Six-hotfix annotation staging / installation | `tools/AnnotateContentHotfixes/`, `Aurora.Tests/Tests/LocalCorrectionProvenanceTests.cs` |
| Neutral preparation/review foundation | `Builder.Data/Content/Review/ContentReviewContracts.cs`, `CanonicalContentAnalyzer.cs` in that directory; `Aurora.Tests/Tests/ContentReviewContractTests.cs` |
| Bundling / current binary | `tools/publish-translator.ps1`, `Aurora.App/BundledTools/AuroraTranslator/AuroraTranslator.exe` |

Sibling checkout at the time of this handoff:
`C:\Users\Ralla\source\repos\5eApiTranslator`; its writer is
`5eApiTranslator/AuroraSqliteImporter.cs`. Current CLI interface:
`sqlite-import <content-root> <sqlite-path>`.

Supporting Lights documents and recorded evidence:

- [Lifecycle implementation](../../Aurora-Lights/docs/local-correction-implementation.md) and
  [accepted correction policy](../../Aurora-Lights/docs/local-correction-policy.md).
- [Preparation/writer boundary, review contract and XMLHelper reuse](../../Aurora-Lights/docs/content-preparation-contract.md).
- [Identity feasibility and historical audit](../../Aurora-Lights/docs/element-identity-feasibility.md)
  and [current roadmap](../../Aurora-Lights/docs/ROADMAP.md).
- [Import ownership audit](../../Aurora-Lights/docs/sqlite-import-ownership.md): historical; its earlier
  reload-failure and executable/XML-only recommendations are superseded above.
- [Metadata compatibility investigation](../../Aurora-Lights/docs/correction-metadata-compatibility.md),
  [CLI evidence](../../Aurora-Lights/docs/correction-metadata-cli-evidence.json), and
  [shape evidence](../../Aurora-Lights/docs/correction-metadata-shape-evidence.json).
- [Archive ledger](../../Aurora-Lights/docs/content-cleanup-2026-09-13.md),
  [hotfix ledger](../../Aurora-Lights/docs/content-id-hotfixes.md), and
  [final post-hotfix nonlocal scan](../../Aurora-Lights/docs/duplicate-hotfix-after.json). The hotfix
  ledger's lifecycle limitations describe the then-unmarked installed copies;
  the subsequent managed lifecycle is documented here.
- [Original seven-conflict comparison](../../Aurora-Lights/docs/remaining-conflict-table.md) and
  [original duplicate-pattern evidence](../../Aurora-Lights/docs/duplicate-pattern-evidence.json).

Latest deployment includes only the six local XML annotations and recoverable
backups. The supporting runtime provenance fix is included in Lights checkpoint `624b6b7`.
No production database refresh, app deployment/reload, push, snapshot
publication, or upstream PR has been performed by this annotation step.

Checkpoint staging retained original trailing whitespace in two source-derived XML fixtures; all other staged whitespace checks passed. Temporary databases and logs were excluded.

## 11. Standalone Translator first slice — September 13, 2026

### Implemented integration

- `Program.ImportAuroraToSqlite` now orchestrates preparation and a candidate
  import. All standalone XML imports use this path, including imports without
  managed corrections and imports that remove the last correction file.
- `5eApiTranslator/Content/LocalCorrectionDocument.cs` is byte-for-byte identical
  to the Lights evaluator at `624b6b7`. Embedded baselines, v1 declaration
  fingerprints, explicit review, rename/removal selection, parent-grant groups,
  unclassified effects and unchanged-companion behavior use that implementation.
  This is a source port with recorded provenance, not shared-library packaging.
- `ContentPreparation` captures raw XML bytes and hashes into temporary roots
  before evaluating them. It writes effective declarations into the staged origin
  and leaves staged local bookkeeping without a second gameplay copy. Real local
  and upstream files remain untouched during preparation/import.
- Differing definitions of the same exact ID stop preparation with
  `duplicate-element-id`, both file paths, declaration ordinals and fingerprints,
  plus an instruction to review and supply an explicit correction. Identical
  structural declarations are consolidated, retaining every occurrence's
  provenance. When suppliers differ in package availability, preparation chooses
  an enabled supplier using existing package identity rules. Differing ID spellings that current consumers could conflate are
  rejected for review; no new normalization policy is applied. Different IDs with
  equal display text remain separate.
- `AuroraSqliteImporter.ImportFinalized` rejects empty/duplicate catalog IDs
  before opening SQLite. `PreparedContentWriter` validates the complete expected
  element-ID set, rejects unexpected/duplicate rows, checks resolution-cache
  coverage against enabled/disabled packages, and persists declaration provenance.
  Prepared imports preserve custom package precedence. Preparation has no SQLite writes.
- `LocalCorrectionSync` backs up the working database to a sibling candidate,
  preserving existing settings. It imports there, checks integrity and foreign
  keys, persists mirrors, checks that the complete installed XML input set and
  hashes still match, and only then activates the candidate. Failure before
  activation preserves the working database. The port retains Lights' recoverable
  retirement after activation and removes obsolete active mirrors on the next
  successful import. Callers must serialize database writers; this does not add
  cross-process import locking.

### Storage and acceptance boundary

The three Lights tables (`local_override_files`, `local_corrections`,
`local_correction_inputs`) preserve the same columns and meanings described in
section 5. `content_declaration_provenance` adds `file_path`, `input_sha256`,
`effective_ordinal`, `aurora_id`, `declaration_fingerprint`, and
`effective_declaration_xml`; its key is `(file_path, effective_ordinal)`.
Ordinals refer to the effective document before identical declarations are
removed. For corrected origins, the input hash identifies captured upstream bytes
while effective XML/fingerprint identify the prepared declaration. The correction
mirror separately retains baseline, local and upstream XML. Do not treat the
effective declaration as a pristine upstream definition.

This additive provenance table is refreshed from the current input set. It does
not introduce durable repository IDs, a normalized base/override schema or the
global canonical uniqueness migration. Source-file ownership still uses the
existing writer, with preparation rebuilding the surviving supplier's staged
declarations when the previous supplier disappears. Runtime/package maintenance
and legacy direct `Import(catalog, ...)` callers are not migrated by this slice.

**No verified-download evidence is produced or consumed.** An installed match
remains `review-pending`, including a complete grouped repair. The imported
`AcceptUpstream` API requires explicit review and matching local/upstream hashes;
cross-file partial group acceptance is rejected during preparation. No new CLI
acceptance command or UI is provided. Retirement requires the evaluator's whole
file redundancy decision and successful activation; unique local content and
disabled files are retained. No publication evidence is fabricated in SQLite.

### Explicit limitations

- Translator's existing catalog builder does not execute document-level
  `append`. This path now rejects active files containing `append` with a file
  diagnostic instead of silently discarding their effects. Append preparation
  must be added before using this route for such a corpus. Other existing
  catalog/parser capability limits remain; ID/cache validation is not proof of
  every gameplay mechanic. Root and operational evidence in managed files stays
  available in the mirror, but this is not a new general operational XML engine.
- Unmarked differing local definitions require review rather than inferred
  override priority. Malformed XML, unsupported element types that fail to reach
  the candidate, ambiguous metadata and source paths fail conservatively.
- The CLI still accepts one content root. Multi-root preparation plumbing exists,
  but a CLI root-registration/catalog adapter is deferred. Automatic authoritative
  downloads, reference scanning, advanced review UI, option-collapse changes,
  canonical schema migration, Android validation and shared packaging remain out
  of scope.
- Database-only package enable/disable commands reject prepared databases with
  declarations consolidated across files, before changing the database. Those
  changes require a preparation-aware package workflow so an enabled supplier can
  replace the disabled representative. This guard avoids silently hiding content;
  the full package-maintenance adapter remains deferred. Existing databases
  without this preparation/provenance state retain their legacy package path.
- Snapshot/reader contract versions are unchanged. Existing optional SRD JSON
  import is retained inside the candidate writer; the new captured-input and
  staleness contract covers XML, not that separate JSON import path.

### Regression evidence and deployment

The four `CorrectionMetadata` XML fixtures, README and provenance JSON were
copied unchanged from `624b6b7`. Tests reconstruct small malformed baselines from
those repaired blocks; they do not claim to reproduce the complete six archived
files. A separate XGTE Staff definition tests provenance-scoped survival.

Focused command: `dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj --no-restore -- correction`.
The suite covers Staff coexistence, the three repaired parent-grant targets,
fingerprinted musketball removal, v1 fingerprint stability, companion updates and
removal, disk-only incorporation remaining pinned, mirrors/local deletion,
explicit/stale/grouped review, failed import versus successful retirement, mixed
local content retention, ignored files, malformed metadata, origin traversal,
unresolved conflicts, failed/raced/invalid candidates, identical supplier
consolidation and removal, append rejection, the finalized-writer guard, and a
separate standalone CLI process.

**Initial result: 9 focused tests passed**, including a separate CLI process. The
fixture candidate contained nine distinct elements, both Staff definitions, all
three repaired grant links and seven pinned operations across four mirrored
files. The build passed with zero warnings/errors; the final focused run rebuilt
the modified source successfully. `git diff --check` passed for tracked edits;
copied source fixtures retain their original intentional trailing whitespace.
The evaluator's SHA-256 matches the checkpoint file exactly. All test
inputs/databases use temporary directories. No snapshot, production database
refresh, installed XML edit or Lights bundle update was performed. The initial
implementation was left uncommitted; source-control publication was subsequently
authorized by the user after pre-commit review.

Pre-commit review found and fixed package-availability integration issues:
intentionally disabled packages were incorrectly required in the resolution
cache; custom precedence was overwritten; and an enabled identical supplier could
be hidden by the disabled stored representative. Two new focused regressions
failed before their fixes and passed afterward. Preparation now considers the
existing enabled-package set, and the finalized writer preserves package settings.
The database-only availability guard described above prevents bypassing that
supplier-selection step. The complete correction suite passed **11 tests**;
the supplier test was additionally rerun after adding the maintenance guard.
The existing `refreshes spellcasting ownership after package changes` regression
also passed, covering the unchanged legacy package path. The reviewed commit
contains source, documentation and fixtures only; binary/snapshot publication and
production refresh remain separate work.
