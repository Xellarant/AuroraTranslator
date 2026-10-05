# AuroraTranslator data and importer handoff

## Legacy append compatibility — October 5, 2026

This release work targets **Aurora.Content / Aurora.Content.Contracts 0.10.1**, database schema **1**, data **18**, and preparation contract **2**. The October 5 Lights trust pass identified two shared composition mismatches in its pinned 0.9.0 package: `homebrew/a.xml` was applied before `supplements/b.xml`, and appended descriptions were combined with base descriptions. XML/database parity alone could not catch them because both paths used the shared preparation logic. The fixture establishes a rule-order difference and a displayed-description difference; it does not establish changed character statistics.

Implemented behavior:

- Import preparation and prepared runtime projection order appends by the source-relative Legacy directory ladder (`supplements` before `homebrew`), with a directory's own files before its descendants and append nodes in authored order within each file. Rule multiplicity and order are preserved. Alias precedence is unchanged.
- Appended `<description>` nodes never transfer to the target, including when the base has no description. Supported mechanics in the same operation still apply. The original operation XML, including ignored prose, remains in provenance.
- Catalog detail `AppliedAppends` lists operations in application order, with their original zero-based file ordinals and source classification.
- Older prepared databases must be refreshed from XML with `ContentImport.ImportAsync`. Current readers reject the old preparation contract; package/schema administration cannot mark old composition as data 18 without import.
- An unreadable supplier cannot cause already-composed effects from the old append policy to be copied into a new-policy database. Upgrade blocks with repair guidance and preserves the working database when old append effects must otherwise be retained. The check includes remembered append dependencies and prior retention that changed operation rows to skipped. Unextended old definitions and definitions already composed under the current policy retain the existing best-effort recovery behavior.

Limits: the shared library uses deterministic ordinal ties for files in one directory and sibling directories. Legacy uses unsorted filesystem enumeration there, so this is an approximation rather than a universal Legacy ordering guarantee. Configured priority between multiple roots is not represented by this slice. No further setter/spellcasting append policy, duplicate-ID policy, correction acceptance rule or app filtering preference is changed.

Verification: the Release build succeeded and all **131 test cases are verified**: the full run passed 130 and exposed one historical assertion that package administration advances the data version; after updating it to require the version-refresh diagnostic while retaining the spellcasting fidelity checks, its focused rerun passed (exit 0). Both direct Legacy oracle cases passed against the updated library: present and absent base descriptions remain unchanged, and supplement grants precede homebrew grants. The original pinned-package reproduction failed as reported. Package verification follows the clean source commit; see the [0.10.1 release record](package-release-0.10.1.md) for commands, results and artifact provenance. Fixtures are disposable; installed XML, production databases and Lights source files were not changed. Consumer adoption belongs in the Lights context.

## Historical catalog summary/detail reader release — September 29, 2026

`Aurora.Content.ContentCatalogReader` now exposes typed `ReadSummaries` and `ReadDetail` APIs over the existing prepared database. The [reader contract](catalog-reader-contract.md) documents fields, examples, identity/alias rules, provenance and boundaries. Summary queries join the effective winner rather than all declarations sharing an ID. Distinct IDs remain separate even when names match; provisional conflicts retain their resolution kind and superseded count. Saved source preferences do not filter results. UA remains first party, local/homebrew content remains homebrew, and published non-Wizards supplements remain third party according to the existing import classification.

Detail exposes imported description markup, finalized XML, surviving base suppliers, applied append provenance, and explicit grant/Companion links. Missing and wrong-type targets remain visible; grant conditions are preserved. No name-based Information/parent matching or character eligibility evaluator is introduced. Each read uses one SQLite snapshot, requires current prepared schema/data **1/17**, and reports incompatible databases with refresh guidance. Live XML overlays and cache invalidation remain host responsibilities. No writer or correction-acceptance policy changed.

Verification: the Release solution build succeeded and the **full Release suite passed 124 tests, 0 failures, exit code 0**, including the five new catalog tests and expanded Staff/Devout/Tatsumi correction fixture. Coverage includes effective versus superseded definitions, aliases, unavailable targets, distinct same-name IDs, conditional/appended grants, source classification, co-suppliers, preference independence, unchanged disk/database reads, freshness boundaries, and fresh versus incremental import equivalence. The build reported the two existing nullable-context warnings at `AuroraSqliteImporter.cs:3307-3308` and NU1900 because the NuGet vulnerability feed was unavailable. No production-database or Lights integration run was performed.

Release state: both **0.10.0** packages were built locally from clean commit `61a0654086e3884793ca156377bf8068a4efc27a`, with schema/data **1/17** unchanged. A disposable external consumer restored the actual packages into an isolated cache and passed **9 smoke checks, exit code 0**. Package identity, dependencies, assembly/source versions, repository commit metadata and SHA-256 hashes were verified; [release verification](package-release-0.10.0.md) records the exact artifacts and warnings. The existing **0.9.0** build from clean commit `d35837845ff663c3612d9e1b8678ea58cc0cefd2` remains recorded in Lights' vendor manifest and predates this API. These new packages have not been pushed, published to a feed or adopted by Lights/Web. Installed XML, production databases and Lights files were not changed. Full Compendium facets, consumer integration, multi-root imports and broader review APIs remain follow-ups.

## Runtime definition decisions and conflict review — September 26, 2026

**Accepted policy:** when different sources define the same exact Aurora ID differently, best-effort import keeps one version usable and flags the disagreement for intervention. The usable choice is provisional, not approval of the duplicate. Diagnostics identify the ID, competing files and selected supplier, and ask for corrected XML or distinct IDs for genuinely different content variants. Repeated imports keep that review issue until the disagreement is resolved. Distinct IDs remain independently usable. The previously accepted AuroraLegacy repository succession rule remains separate from an unresolved conflict between independent sources.

The five commits through `21af7e9` changed the September 25 implementation: readable collisions follow Aurora Legacy load order; retention of previous effective XML applies to unreadable suppliers; superseded declarations have inspectable database rows while only the selected definition supplies active mechanics; content-authored and curated forwarding aliases can map absent old IDs to live IDs. Current database schema is **1**, data version **17**. The subsequent `ab3e211` release bump moved the package property to **0.9.0**; the clean `d358378` build includes these changes and the runtime decisions below. Immutable 0.8.1 artifacts built from `ce61268` predate them.

This follow-up implements the user's declaration-level runtime decision policy:

- Editing an unrelated entry no longer invalidates a provisional or established-successor duplicate decision. Runtime matching uses the existing declaration fingerprints for each exact ID and supplier, after correction evaluation. Inserting or moving other IDs is allowed even when absolute declaration positions change.
- Every file participating in the runtime read is compared before exclusions are applied. A changed winner, changed rejected definition, new competitor, or changed order/count of same-ID declarations invalidates that ID's exclusions together. Other IDs keep their decisions. A newly conflicting runtime read names the suppliers and requests a database refresh and review; the next successful best-effort import can publish a provisional choice with the conflict still flagged.
- Removing or repairing a conflicting definition can release the old exclusion. Runtime reads do not rewrite the database or clear its existing review report. The import lifecycle remains responsible for persisting new decisions.
- Database freshness still uses whole-file fingerprints. Reading changed local XML without reviving an unchanged duplicate does not make the stored database current. Ordinary primary files outside the runtime overlay continue to come from the database until refreshed; this is not an incremental database-write optimization.
- Recovery of retained effective XML from unreadable suppliers, whole-file skips, and rejected append operations keeps its existing revision checks. This slice does not broaden those recovery decisions to declaration-level matching. Protected correction validation and grouped acceptance remain unchanged.

The runtime change needed no schema migration: it reads the declaration provenance already persisted by the current contract. It was subsequently committed as `d358378` and included in the consumed 0.9.0 packages; the implementation itself did not modify installed content or production databases.

Verification: `dotnet build --no-restore -m:1 -p:UseSharedCompilation=false` succeeded with **0 errors** and the two pre-existing nullable-context warnings at `AuroraSqliteImporter.cs:3307-3308`. **22 focused tests passed**: three new `runtime decisions` cases, five `resilient conflicts` cases, the expanded correction-incidental-copy case, seven `forgiving imports` cases and six `skip projection` cases. Coverage includes unrelated edits and insertions in both winning/rejected files, shifted declaration positions, changed winners/rejected copies/new suppliers, within-ID order/count changes, removal and convergence, persistent review diagnostics, corrected upstream companions, default user overlays, and unchanged database freshness checks. Verification used disposable test content; the full suite was not rerun for this bounded change.

## Best-effort imports and established successors — September 25, 2026

This historical checkpoint superseded the September 24 blanket conflict rejection below; the September 26 section above updates its selection and runtime-decision behavior. The September 25 release declared Aurora.Content / Aurora.Content.Contracts **0.8.1**, schema **1**, and, after the review fixes below, data version **15**. The additional data version prevents older readers from silently ignoring retained/rejected-definition and unreadable-input decisions. Refresh an existing prepared database to migrate; no installed XML is rewritten. The previously vendored 0.8.0/data-14 packages do not contain these follow-up fixes; consuming the fixes requires the new immutable 0.8.1 packages built from a clean committed tree.

### Import resilience review fixes — September 25 follow-up

- Preparation validates declarations with the same typed parser used to construct the catalog. Missing names and invalid Boolean/integer fields now produce original-file/declaration diagnostics before SQLite writing. Ordinary invalid files use the existing file-level skip/retention policy; protected correction inputs still block. Typed append effects are validated before replacing a target, so a bad operation can be skipped while valid operations in that file continue. Arbitrary writer failures still refuse activation.
- Spell descriptions beginning with `At Higher Levels.` no longer request a negative substring length. Raw description XML remains preserved.
- An unreadable known supplier cannot authorize AuroraLegacy succession over an unresolved independent competitor. Keep the prior effective definition until repair or deliberate removal resolves that supplier.
- Retention includes previously applied append dependencies. An unreadable extension preserves its target's previous effective XML through repeated imports; repair/removal releases retention and current appends apply once. Definition suppliers and append suppliers are recorded separately so a broken append cannot resurrect a deliberately removed base definition.
- **Explicit user decision:** if a file's bytes cannot be read and there is no verified prior copy, best-effort import skips it with an unreadable diagnostic, including unknown files under `user/local`. Previously working ordinary content is recovered when possible. Known correction files and authoritative files referenced by readable corrections remain protected. This exception concerns failure to capture bytes, not readable malformed correction metadata or malformed local XML.
- An uncaptured input is recorded with the explicit `unreadable` marker in `local_correction_inputs.sha256`, not a fabricated digest. Runtime readers honor that capture failure while access remains unavailable. Once readable, the file is eligible for evaluation and makes the database stale. If it becomes readable between capture and activation, the candidate is refused and the existing database preserved so the next import can include the new input.
- The standalone CLI exposes `sqlite-import [auroraPath] [sqlitePath] [--skip-unusable]`. Strict mode remains the default; the switch enables the same policy as the in-process API.
- At this checkpoint, the runtime declaration decision question was pending. The September 26 section records the user's answer and the implemented boundary.

Added regression cases cover typed failures and cached recovery, heading-only spell descriptions, typed append failures, three-way successor collisions, unreadable append dependencies, deliberate base removal, unknown/known locked inputs, correction protection, an input becoming readable during activation, data-14 migration, and the CLI switch.

Verification: the build completed with **0 warnings and 0 errors**. The full 111-test run passed 108 tests and exposed three stale assertions expecting data version 13. Those assertions now check the current library contract; all three passed on focused rerun. The seven new regression cases passed, including repeated import/runtime recovery and migration. All 111 tests are therefore verified across the full run and focused reruns. The protection test also covers a readable correction referencing a locked source before any database exists. Checks used disposable fixtures and repository regression data; no installed XML, production database, or Aurora Lights files changed. Release packaging targets `artifacts/packages/0.8.1` after committing these changes; the local package manifest records the source commit, clean-tree provenance, and SHA-256 hashes. Vendoring into a consumer remains work for that repository.

- Prefer AuroraLegacy over archived aurorabuilder definitions of the same exact ID when their XML update-file URLs identify `AuroraLegacy/elements` and `aurorabuilder/elements` on `raw.githubusercontent.com`. This is the explicitly approved repository succession rule, not a general trust score. It does not decide between unrelated repositories or divergent AuroraLegacy candidates. Distinct archived definitions remain available. New versions from the established successor can replace earlier accepted definitions.
- With skip enabled, inspect every declaration before selecting. Ambiguous exact-ID collisions keep the previous **effective XML**, including previously applied append effects. Unaffected definitions update normally. Do not apply current append operations to retained definitions: that would silently change the supposedly preserved mechanics.
- With no previous working definition, select a **provisional** candidate in ordinal order of normalized relative path, then declaration position (absolute path is only a final tie-break for multiple roots). This is deterministic, not a claim that the choice is authoritative. Log and persist the competing declarations as skipped collisions for later review. Identical declarations still consolidate silently with their provenance retained.
- Retention survives restarts and repeated refreshes, including a supplier becoming unreadable or all known suppliers becoming unreadable. Preserve cached effective definitions when available; no synthetic declaration is claimed to have come from broken current XML. Remember unreadable supplier paths until repair/removal permits convergence. A successfully parsed sole remaining declaration, identical converged declarations, or an established successor releases retention.
- An unreadable file's cached content can be recovered without importing its malformed current bytes. The current file is still reported unreadable. Never rewrite source files or promote an alternative merely because a competing file failed to parse.
- Correction protection is unchanged: invalid metadata, ambiguous correction origins, inconsistent review groups and conflicts involving explicitly protected IDs still block activation. A provisional/retained companion prevents retirement of its local correction file until resolved. Local XML is not automatically an override.
- Case/padding variants are still unavailable rather than silently rewritten into one ID. Unrelated content still imports. With skip disabled, unresolved first-import conflicts remain unavailable and new unresolved refresh conflicts still block. The established AuroraLegacy succession rule applies in both modes.
- Aggregate index directories are containers, not publishers. Classification uses a file's own Source declaration, referenced publication, or nearest containing Source declaration; never all unrelated Source records under an aggregate root. Ambiguous publisher flags in skip mode retain game content and report a classification issue. Missing/ambiguous classifications use the existing neutral `local` database bucket, without claiming homebrew/official authorship or treating the content as a correction.

Persistence and reader contract:

- `content_declaration_provenance` retains current valid accepted and rejected declarations.
- `content_rejected_declarations` records supplier path, captured hash, ordinal, ID and XML for runtime exclusion of that exact rejected revision.
- `content_definition_resolutions` records provisional/retained/successor decisions, the chosen supplier and effective XML. `content_definition_suppliers` keeps paths and `supplier_kind` (`definition` or `append`) needed for retention across unreadable refreshes. Data-13/14 inputs migrate during candidate preparation; old supplier rows are treated as definition suppliers.
- `content_prepared_elements` contains the actual selected/retained definition. `content_skipped_files.kind` now also reports `definition-collision`, `superseded-definition` and `classification`; these are **not whole-file exclusions**. `unreadable` still excludes the captured broken runtime file. Consumers must not discard an entire file for a declaration-level collision.
- Runtime XML reads omit the exact rejected revisions and restore retained effective definitions. New/different XML revisions are re-evaluated, not silently dismissed by a stale path-only decision. Appends to retained definitions remain recorded as skipped so they cannot replay at runtime.
- Settings distinguishes review issues from successful successor replacements and limits the initial issue display to 30 entries, with access to the full report. SQLite remains the performance cache; ordinary ambiguity must not prevent best-effort availability.

Validation: focused library fixtures cover provisional selection, same-file collisions, unaffected content, effective-XML retention, data-13 migration, repeated imports, unreadable suppliers, repair, authority updates, SQL/runtime parity, aggregate classification, correction protection and retirement. An isolated fixture containing the actual public Oathbreaker and original/Legacy DMG Source files imports 12 elements and classifies Oathbreaker as official. This is not a full master-index or Android-device run. Package provenance must remain marked dirty until source changes are committed and a fresh immutable version is vendored for release.



### Aurora Lights verification

The vendored 0.8.0 packages passed **41 focused Aurora.Tests tests** for import reporting, database trust, prepared projections and correction metadata. The updated `tools/ContentDatabaseRehearsal/run-policy-checks.ps1` passed **225 assertions in 22 separate processes**, exercising the actual sync and full-loader services with skipping both enabled and disabled. First-install choices, retained definitions, unaffected updates, repair, restart persistence and protected-correction rejection all behaved as intended. Results: `buildtmp/content-policy-smoke-20260925-134305-4d3dae/summary.json` in Aurora Lights. The focused shared-library tests and actual upstream Oathbreaker fixture also pass. Live content, settings and character saves were not modified.

The full recursively fetched master index still needs an Android-device refresh with this build. The installed older APK cannot acquire the new policy solely through a content download. Do not clear app data again to address these known library failures.


Android ARM64 Debug build also passed: dotnet build Aurora.App/Aurora.App.csproj -f net10.0-android -r android-arm64 -c Debug -m:1 -p:NuGetAudit=false (0 warnings, 0 errors). Device runtime and the full master-index refresh remain unverified.

## Automated lifecycle verification — September 24 follow-up

The previously manual first-import, rejected-refresh, and repair/restart checks
now have a repeatable Lights rehearsal runner:
`tools/ContentDatabaseRehearsal/run-policy-checks.ps1`.
It exercised actual app sync/reporting/full-loader services against disposable
fixtures in **22 separate processes with 222 passing assertions**, covering both
values of the skip-content setting. First-import reports named the unavailable ID
and both suppliers; unrelated definitions and their grant remained loaded.
Rejected new conflicts and invalid protected metadata preserved exact database
bytes and the live catalog. Repairs cleared conflict reports, restored the
canonical definition, and preserved correction intent after a process restart.

Separate cold-start probes confirmed that invalid correction metadata still on
disk prevents a fresh load despite a valid preserved database. The prepared reader
re-evaluates runtime local XML; the raw XML fallback also rejects invalid marked
corrections before publishing (source-verified). Repairing metadata and refreshing
restores successful fresh-process loading. No last-known correction fallback or
new authority policy was introduced. This limitation is distinct from activation
protection and requires a separate policy if offline recovery is desired.

Evidence and scope are recorded in Lights
`docs/content-conflict-policy-2026-09-24.md`; results are in the local artifact
`buildtmp/content-policy-smoke-20260924-122340-85b9b7/summary.json`.
These checks did not render MAUI or exercise character-tab navigation, PDFs, or
installed platform packages. No live content or saves changed. The three
functional checks no longer require manual repetition.

## Historical conflict and correction policy — September 24, 2026

Historical snapshot, superseded by the September 25 policy above. This section superseded the earlier skip-policy discussion and implementation
status below. The staged implementation targets **Aurora.Content and
Aurora.Content.Contracts package 0.7.0, database schema 1, data version 13**.
Package version, schema version, and data version are separate contracts. Data
version 13 records unavailable identities; a consumer must honor that contract
rather than load it as an older projection that could resurrect quarantined
definitions. Lights obtains the current versions from the library. The strict
reader still requires a successful refresh of an older data-12 database before
loading it; preserving the file on failure is recovery protection, not an
older-format compatibility guarantee. Empty/unusable existing database files
are not automatically deleted to obtain first-install behavior.

### Conflict handling

- **First installation, with no existing database:** inspect all suppliers before
  choosing any canonical representatives. Identical same-ID declarations may
  consolidate with provenance retained. Different definitions of one identity
  make **every conflicting definition unavailable**; no file-order, timestamp,
  source-priority, or first-insert winner is selected. Import unrelated definitions
  from the same files normally. Case/whitespace spelling collisions receive the
  same unavailable treatment; authored IDs are not silently rewritten.
- **Refresh with an existing database:** a newly encountered conflict blocks
  activation and preserves the complete existing database. Do not partly publish
  unrelated updates beside an arbitrarily selected definition.
- **Already unavailable identities:** their continued conflict does not prevent
  later refreshes from updating unrelated content. Resolution and a successful
  refresh can restore availability. However, skipping an unreadable file that
  previously supplied a quarantined identity does not prove resolution: that
  refresh blocks and preserves the existing database rather than promoting the
  remaining supplier. Old declaration provenance identifies these suppliers.
- Persist unavailable identities in `content_unavailable_elements` and retain all
  conflicting declarations and supplier evidence in
  `content_declaration_provenance`. Reports identify the affected IDs and files.
  This is a definition-level quarantine, not rejection of all content in those
  files. Appends to unavailable identities remain inspectable and unapplied.
- The same unavailable mask applies to database projections, raw runtime XML,
  secondary-root content, and intrinsic/built-in host definitions. Trimmed,
  case-insensitive availability checks match spelling-conflict detection without
  changing persisted IDs. A runtime file or built-in definition cannot become a
  substitute winner. Referencing grants and selections do not acquire an invented
  replacement definition.

### Protected intent and ordinary local content

Local XML is not automatically an override or a correction. Well-formed unmarked
local content participates as ordinary content; it replaces another definition
only under an explicit supported policy. Embedded correction intent continues to
be evaluated before canonical preparation and mirrored to the database.

Invalid/unsupported correction metadata, missing or ambiguous correction origins,
overlapping managed corrections, inconsistent linked groups, or failed protected
correction evaluation **block activation**. Generic unreadable-file skipping must
not silently release protected intent or activate the upstream definition instead.
Malformed local XML whose correction intent cannot be determined also blocks;
absence of intent cannot be established through a substring search of damaged
XML. An unresolved conflict involving an explicitly corrected identity blocks
even on the first installation. Local files are neither deleted nor automatically
accepted as upstream repairs through conflict handling.

### Evidence, deferred work, and verification status

The September 24 read-only installed-content scan found **20,771 non-local
declarations representing 20,766 IDs: five structurally identical repeated IDs
and zero differing canonical definitions**. Local correction overlaps are a
separate category. Historical duplicate installations and repeated rehearsals of
the same corpus are not independent upstream-update events, so these counts do
not establish a probability of encountering a conflict on a refresh. There is no
empirical basis here for asserting the proposed **greater-than-15% per-refresh**
threshold is exceeded.

Selective preservation of only affected last-known-good definitions while
publishing the rest of an update is deferred; the existing-database policy remains
whole-candidate refusal for new conflicts. The advanced diff/resolution UI and
physical `UNIQUE(aurora_id)` migration also remain deferred. Preparation and
candidate validation enforce canonical availability without claiming that the
physical schema migration has been completed.

Verification: **50 distinct Translator scenarios and 76 focused Lights tests
passed**, including first-install exclusions, existing database/input hash
preservation, correction failures, spelling variants, runtime/host/generated
element masks, repairs, and deferred retirement. The staged Translator build had
zero warnings/errors. Lights tested the vendored 0.7.0 package. Package provenance
is recorded in Lights `vendor/nuget/manifest.json`, with `dirtySource: true` over
base commit `1a4a93a`; this is a local development artifact, not a published release.
Both repositories remain uncommitted. Manual installed-app/UI/PDF checks remain
pending. No installed content, character saves, or live database was changed.

## Historical shared-library follow-up — September 23, 2026

This section supersedes historical implementation status below. Aurora Lights now
uses the shared `Aurora.Content` library in process. The copied `Aurora.Importer`
and bundled executable were retired in Phase 7; source restrictions apply at
runtime over the full catalog. Local XML is ordinary content unless it explicitly
carries correction intent. The full implementation history and parity evidence
are maintained in [the library split plan](../../Aurora-Lights/docs/translator-library-split-plan.md).

The September 23 follow-up addressed the 0.6.0 import/read mismatch for 0.6.1:

- A persisted append whose status is `skipped` must not be applied by the prepared
  reader. Runtime XML must likewise omit the exact rejected operation from the
  unchanged input revision, without dropping valid definitions or other appends.
  An edited/repaired local file is evaluated anew; an old skip is not a permanent
  blacklist against its path.
- The library exposes its current schema/data versions, and Lights uses those
  values instead of maintaining a separate literal version gate.
- Lights trims padding on grant references at parsing time, preserves the source
  XML and declaration IDs, and resolves the trimmed reference during progression.
- PDF identity lookup uses the full database catalog, including parent races,
  regardless of obsolete `content_packages.is_enabled` flags.
- Release workflows no longer restore the deleted importer; CI triggers include
  the shared package pin/feed and DataIntegration changes.
- Refresh reporting distinguishes rejected files from rejected append operations.

**Historical policy gap, superseded by the September 24 rules above:** skip-enabled
imports then retained the first conflicting declaration in path order and skipped
the later file; they could also omit invalid/overlapping correction files and
proceed without their corrections. Those behaviors were not an authority decision
or acceptance of an upstream repair. The September 24 policy replaces them with
first-install definition quarantine and existing-database preservation; selective
last-known-good preservation remains deferred.

**Not implemented by this follow-up:** verified-download automatic correction
acceptance, advanced diff/resolution UI, a physical `UNIQUE(aurora_id)` migration,
or repairs to the separately recorded pre-existing character round-trip issues.
Canonical uniqueness continues to be checked during preparation and candidate
validation. No live content, character saves, or installed database is changed.

Validation and package provenance for this uncommitted checkpoint are recorded in
[the September 23 fix record](../../Aurora-Lights/docs/content-library-fixes-2026-09-23.md).
Manual installed-app, UI/PDF, and target-device verification remain required.

---

**Latest work, September 14, 2026:** the append/unrestricted-catalog implementation,
protected alias drafts, successful isolated verification, and remaining rollout work are in
[the append follow-up](append-preparation-2026-09-14.md) and section 20. Earlier
enabled-source behavior below is historical and is superseded by section 20.

**Decision baseline: September 13, 2026.** Audience: AuroraTranslator maintainers
and the future shared importer library. This document consolidates the accepted
direction and the current Aurora Lights implementation. It is intended to travel
with a handoff; the essential rules are included here rather than only linked.

**Status:** Aurora Lights checkpoint `624b6b7cec82e8ad6d0efffb36ba414908469a15`
(`Add protected local corrections and content review foundations`) records this implementation on `main`.
The Lights working tree was clean at that checkpoint; the refresh-validation follow-up is now committed as `02d56c6`; the performance follow-up in section 13 remains uncommitted. Nothing was pushed or released; no version bump or
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
Latest content deployment: Stoneheart's user-authorized local spell-reference
correction was installed September 14; see section 18. The earlier six known
hotfixes carry embedded v1 metadata
(ten review-pending operations). See sections 7–9 and the
[annotation record](../../Aurora-Lights/docs/hotfix-metadata-annotation-2026-09-13.md).

Latest production refresh attempt, September 14: preparation rejected installed
append operations before activation. Production remains unchanged; section 19
records both refresh blockers, the record audit and all 59 passing regressions.

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
SQLite integrity/foreign keys and corrected IDs under their authoritative
source-file provenance in `elements`. Only enabled packages require corresponding
IDs in `resolved_elements_cache`. It then
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

**September 29 status note:** this sequence records earlier planning. Shared-library extraction and publication through 0.9.0 are complete, and the summary/detail reader above is now implemented in source. Data 17 deliberately retains superseded declaration rows: any future canonical uniqueness migration must distinguish effective identity from declaration history. Do not add an unconditional unique index on `elements.aurora_id`. Availability/source restrictions remain app-layer filtering over the unrestricted prepared catalog.

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


## 12. Lights refresh validation and app availability follow-up (September 13, 2026)

A live-app refresh reported Tatsumi missing from the candidate. The installed DB
already contained `ID_RGTTYR_RACE_TATSUMI`; Ryoko's package had `is_enabled=0`, so
its definition was intentionally absent from the derived resolution cache. The
Lights wrapper incorrectly treated that filtered cache as the complete import.
The stored flag does not establish who changed it or when.

Lights now validates corrected IDs in `elements` against the expected authoritative
source file, regardless of package availability, and checks cache presence only
for enabled packages. A same-ID declaration from a different file cannot mask a
missing corrected definition. Missing data still rejects candidate activation
and preserves the working database and local corrections.

The user's clarified intent is that all eligible content remains in the catalog;
enable/disable choices govern app availability, not eligibility for import.
Currently Settings > Content > Sources persists those app choices in the local
`content_packages.is_enabled` column and rebuilds the filtered cache. This fix
does not relocate those preferences or enable Ryoko's. Do not publish personal
availability settings as authoritative content policy or require disabled content
to appear in the app's filtered resolution cache.

The disabled-source regression failed before the Lights fix. All 24 lifecycle
tests passed afterward, and the three new regression cases also passed through
the actual bundled Translator (including both enabled/disabled provenance failures).
Translator's independently recorded availability fixes in section 11 remain
separate; its new executable has not been substituted into Lights by this work.

A full rehearsal copied all 1,189 installed XML files and the working database,
then ran the existing bundled Translator through the corrected Lights wrapper.
The candidate passed integrity/foreign-key and correction-presence checks: 20,973
elements, six mirrored files, ten review-pending corrections, and Tatsumi stored
while absent from the disabled-source cache. Every existing enable/disable flag
was preserved. SHA-256 comparisons confirmed the live DB and all XML unchanged.

The additional assertion that every existing precedence rank remains unchanged
did NOT pass: the bundled Translator changed 51 ranks (49 supplement entries
200->400, UA 200->500, user 400->500). This is separate from the false missing-ID
failure and has not been patched by this bounded Lights fix. Do not describe the
full rehearsal as complete package-setting preservation. Reconcile the bundled
writer's classification/precedence behavior with Translator section 11 before
claiming a fully preference-preserving refresh. No production activation, binary
replacement, commit or push was performed for this Lights follow-up.

Activation semantics clarified: protected definitions are materialized in prepared
XML even if upstream omits them. If an importer subsequently drops an expected
definition, reject the candidate and retain the entire previous working DB.
Intentional upstream deletions of unprotected companions are separate. Per-element
carry-forward into an otherwise refreshed DB is not implemented; it would also
need to preserve related rules/grants/references and validate their consistency.


## 13. Lights scan and correction-tracking performance (September 13, 2026)

The ordinary change scan used to construct the entire XML gameplay catalog before
comparing file hashes. Lights now uses `BuildFileCatalog` to enumerate the same
file paths and root prefixes without parsing definitions. The existing MD5 file
comparison remains; correction-aware scans still use the recorded SHA-256 input
snapshot. Do not substitute timestamps or file lengths for content hashes: edits
with unchanged timestamps and sizes must remain detectable.

The new correction wrapper also committed each input hash separately. It now
records the complete input snapshot in one SQLite transaction with a reused
prepared insert. Keep integrity/provenance validation, post-import input hash/set
revalidation, candidate activation and retirement ordering. This optimization
does not weaken correction protection or permit partial imports.

Temporary-copy measurements using 1,189 XML files:

| Operation | Before | After |
| --- | ---: | ---: |
| Ordinary scan of the same frozen DB/root, three runs | 1.44-1.89 seconds | 35-44 milliseconds |
| Post-import validation, mirroring, activation and cleanup | 12.35 seconds | 2.48 seconds |
| Direct wrapper/import invocation, zero changed files | 43.17 seconds | 33.80 seconds |

The existing bundled Translator still consumed about 27 seconds with zero files
changed. That remains a target for the authoritative importer/shared-library
work; determine which derived rebuilds can be skipped for unchanged inputs and
which are required by writer/schema changes. Do not bypass writer upgrades or
skip validation merely because source files are unchanged. Lights already avoids
calling the importer during UI reload when its change scan reports no changes.
The direct-import measurements above exclude the app's subsequent element reload
and diagnostics. The earlier full import rebuilt all 1,189 files across a data
version transition; failed candidates caused retries to repeat that conversion.

Validation: 29 focused tests passed (24 lifecycle, five change-scan cases). A new
scan test failed before the fix because malformed changed XML was parsed rather
than reported as changed. Tests cover same-size/same-timestamp edits, additional
roots and Windows separators, additions/deletions/renames, and correction failure
and race safeguards. The temporary bundled-importer output retained all 1,189
input records, six mirrored files, ten pinned corrections and valid foreign keys.
The separate precedence issue in section 12 is not resolved by this optimization.

No live database/XML was used as an output target by the performance work. The
live DB did change externally during measurement, so the final scan comparison
used the same frozen temporary DB/root for both implementations. The provenance
fix was committed externally as `02d56c6` during this work; the performance edits
remain uncommitted. No executable was rebuilt/published into the Lights bundle.

**Refresh progress display follow-up:** Lights Settings now keeps a progress bar
visible while checking for changes and reloading content, even when no import is
needed. Preparation, unknown totals and post-import finalization use an animated
indeterminate bar. Known importer progress retains its existing determinate
presentation. The bundled executable currently emits totals only at exit; it
therefore displays "Importing content" with an animated bar, without claiming
zero changed files or remaining stuck at zero percent. Future shared-library or
CLI progress should report actual stages/counts; preserve the indeterminate path
where totals are unavailable. Overlapping check/refresh button actions are
blocked. The Windows app build passed with zero warnings/errors. This small UI
change did not run a live database refresh or a native UI smoke test.

## 14. Measured implications of the 51 changed ranks

The earlier rank warning now has a controlled follow-up. See the full
[priority-impact audit](../../Aurora-Lights/docs/content-priority-impact-audit-2026-09-13.md).
Two copies of the frozen 20,973-element candidate differed only in the 51 ranks.
The actual bundled Translator ran `refresh-package-resolution` on each. All 61
application tables were compared; both outputs passed integrity/FK checks.

- All 19,808 enabled Aurora-ID winners were identical. All 20,873 winners were
  also identical with every package enabled; none of the 100 duplicate pairs
  changed order. Own element identities/definitions did not change.
- Four `features.parent_element_id` values and their four primary-parent
  `element_support_links` changed: Arcane Cord, Elemental Focus, Focused Lightblade,
  and Helm of Heat-vision from Blazing Dawn Player's Companion.
- Their parent changed from `ID_WOTC_ERLW_CLASS_FEATURE_ARTIFICER_INFUSE_ITEM` to
  `ID_WOTC_UA20190228_CLASS_FEATURE_ARTIFICER_INFUSE_ITEM`. The alias `Artificer
  Infusion` resolves by the name `Infuse Item`; the new UA rank beats Eberron.
- This is not equivalent parentage: the four options declare `Artificer Infusion`,
  whereas UA choices require `UA Artificer Infusion`. The old Eberron selection
  itself relied on a final row-ID tie-break, so merely restoring ranks does not
  make that inference semantically robust.
- All other tables were identical apart from the expected rank columns in
  `content_packages` and `resolved_elements_cache`. Grants/rules/selection links
  and correction mirrors stayed unchanged. The four options retained all 40
  selectable memberships: 20 Eberron and 20 Tasha selection rows.

Current Lights `DbElementLoader` reads feature minimum levels, not these feature
parent IDs or primary-parent support links. No current Lights character-choice
regression was demonstrated; the feature/support loader views do expose the
changed parentage. Do not label this as 51 broken sources or a proven blocker for
the progress-display commit. Treat the parent inference and differing package
classification as concrete issues to resolve before using inferred parents as
an authoritative shared-library contract.

Preserving ranks is a compatibility measure. The durable fix should constrain
parent inference by explicit identity and compatible support/edition context,
retaining ambiguity/multiple valid parents where appropriate. Global Aurora-ID
uniqueness cannot resolve these two parents because their IDs differ. The
Translator's path derivation also labels supplements as homebrew and UA/user/reddit
as local; the app displays/sorts package kind. That classification issue was held
constant in this rank-only experiment and needs its own reconciliation.

No importer source, bundle, live XML or live database was changed by this audit.
The audit DLL hash and precise feature IDs are recorded in the linked report.

## 15. Translator parent identity and publisher classification follow-up

September 13, 2026. Sections 12–14 and the linked priority-impact audit were read
before this implementation. Those existing handoff edits were preserved.

### Why parentage depended on names and ranks

Many Aurora child declarations carry a support-family label rather than a parent
ID. The importer stored the first/preferred support token as `parent_support_text`,
then used `parent_family_aliases` to translate labels such as `Artificer Infusion`
to a parent name such as `Infuse Item`. The old feature/archetype queries chose
among candidates by file/package affinity, cache membership, package rank, alias
priority and numeric row ID. Other parent queries combined ID/name matches and
selected `MIN(element_id)`, so even an explicit ID could lose to a display-name
collision. Background features also guessed from declaration order.

These were compatibility heuristics for incomplete relationship metadata, not
explicit XML parent identities. The four-infusions audit proves that the result
was not stable or edition-safe. Restoring package ranks cannot fix that.

### Implemented parent and selector behavior

UA is eligible whenever its actual rules select the option's type and supports.
Section 14's particular UA selector requires `UA Artificer Infusion`; the four
options declare `Artificer Infusion`. That literal mismatch, not UA authorship or
edition assumptions, excludes this particular selector. A UA feature with matching
supports qualifies regardless of its name, package or rank.

`AuroraParentRelationships.cs` supplies one resolver for features, archetypes,
subraces, race variants and background variants:

- Explicit Aurora IDs from all child supports resolve directly. An unresolved ID
  is never converted into a display-name match.
- Parent grant rules and inline selection items match raw target Aurora IDs.
  These relationships coexist with support-based selection relationships.
- Selectors are discovered through their actual rules, without a parent-name
  alias. Static support conditions evaluate against the child's ID and complete
  support set using `AuroraExpressionEngine`, including AND, OR, parentheses and
  negation. Type compatibility is extracted unchanged from the character engine
  into `AuroraSelectionRules` and reused.
- For a subclass selected by a granted feature, explicit incoming grant chains
  lead to its Class. The same mechanism handles Race/Background root columns.
  Cycles terminate through deduplication.
- Every distinct rule-backed candidate is retained. Package rank, names, source
  affinity, declaration order and evidence rank cannot choose between parents.
  The singular parent column is populated only when one candidate remains.
- Name aliases, same-file and declaration-order parent guesses are removed.
  The historical alias table remains for compatibility but is not consulted.
  Implicit display-name matching is also removed from element/select support
  links and the selectable-option index. Explicitly authored inline text choices
  retain their existing handling.

The additive `parent_relationship_candidates` table records relationship kinds
and candidate IDs. Its retained `evidence_rank` field is uniform and has no
selection role. `v_ambiguous_parent_relationships` exposes multiple candidate IDs.
`parent_selector_diagnostics` identifies invalid expressions as `actionable`
with the select ID, expression and parser error; character-dependent expressions
and selectors without static targets are `deferred`, with the missing evaluation
context/evidence explained. There is no arbitrary fallback for these cases.

Full imports and package refreshes rebuild this derived parent graph. The first
package update on an older database triggers full legacy resolution when the
candidate table is absent. Later updates still rebuild parent evidence
conservatively, since a changed grant ancestor can affect a selector several
edges away. Dependency indexing is a future performance improvement.

This is structural relationship evidence, not character-specific availability.
Levels, requirements and character-dependent macros remain the character
evaluator's responsibility. The broader existing dynamic-choice evaluator and
its candidate-cache semantics have not been rewritten in this slice; removing
implicit support/name matches is the selectable-index change. Missing supports
do not establish an exclusive parent. Consumers must handle multiple parents
without substituting a rank/name winner. No new authoring proposal format or
canonical-ID migration is introduced.

### Accepted publisher categories and implementation

The user's clarified categories supersede the old path defaults:

| Content | Stored package kind |
| --- | --- |
| Wizards UA (`unearthed-arcana` / `ua`) | `official` (existing first-party bucket) |
| Core Wizards books | `core` |
| Other Wizards publications, including supplements | `official` |
| Published non-Wizards supplements | `third-party` |
| Explicit user/local/homebrew and Reddit community content | `homebrew` |

`official` identifies the existing first-party category here; it does not turn
UA playtest material into finalized rules. Reddit's `reddit-unearthed-arcana`
directory is community homebrew, not Wizards UA.

`ContentPackageClassification` reuses the Source-element setter vocabulary from
Lights: `core`, `official`, `third-party`, `homebrew`, `supplement`, and author
metadata. It aggregates evidence across the existing package's files and source
references, keeping Wizards supplements first party even though they live under
`supplements`. Explicit local/community roots remain homebrew despite copied
official Source metadata. Published third-party Source metadata can classify an
otherwise unrecognized root such as Ryoko's. Unknown roots default to homebrew;
supplemental/third-party paths default to third party absent stronger metadata.
Conflicting publisher categories inside one package fail with a diagnostic asking
for corrected Source metadata or a separated package; no category wins by order.

Package keys are unchanged. Every XML import refreshes classifications, including
unchanged files, while the prepared path preserves existing enable flags and
custom ranks. New packages receive defaults for the corrected category. The
separate database-only resolution command does not re-read XML classification.
Original Source-element flags are retained as supplied; this is package
classification, not a rewrite of installed XML or proof of publication authority.

### Verification and scope

The initial regressions reproduced wrong UA parent inference, an explicit ID
losing to a display-name collision, and UA classified as local. The follow-up
regression also failed before removing the name gate: a differently named UA
feature with matching supports could not become a candidate. A second assertion
reproduced a display-name collision incorrectly entering its selectable pool.

**Five parent/source tests passed**, covering matching UA across package and name
boundaries, non-primary support tags, name-only rejection, OR/exclusion conditions,
explicit inline IDs, grants alongside selectors, grant ancestry, ambiguity,
package enable/rank changes, complete/legacy refresh, deferred/invalid selectors,
and publisher classification with preserved settings. **11 correction tests**,
**11 choice tests**, and the existing **package/spellcasting refresh test** passed.
Verification used temporary data and existing regression fixtures.

A temporary copy of section 14's frozen `changed-ranks.sqlite` was refreshed with
the updated Translator. Integrity was `ok` with zero foreign-key failures. All four
reported Blazing Dawn infusions retained Eberron and Tasha candidates through
actual select supports, and all **40 recorded selection memberships** remained
identical. Their singular parent fields are null because multiple selectors
qualify. This particular UA selector still does not match; the new UA regression
proves a matching UA selector does qualify.

The refreshed copy has **17,740 parent candidate relationships**. It reports
**172 character-dependent selectors** and **one selector without static targets**
as deferred. All **263 implicit display-name selectable links** were removed.
Compared raw elements, grants, selects, select items/supports, resolved Aurora-ID
winners and both correction mirrors were unchanged. The original frozen database's
SHA-256 was unchanged. This replaces the earlier local name-gated parent audit;
its parent counts and timing are not evidence for the final implementation.

The final isolated database refresh took about **3.5 seconds** on this machine;
this is a diagnostic observation, not a controlled performance benchmark. There
was no live Lights builder end-to-end test. Temporary evidence:
`C:\Users\Ralla\AppData\Local\Temp\translator-rule-audit-c7fst7pz\evidence.json`.

No installed XML, production database, Lights bundle or sibling implementation
was changed. These follow-up source changes remain local and uncommitted; no
snapshot or executable was published. Sections 12–14's pre-existing handoff edits
remain intact.

## 16. Follow-up project audit and corrected regression inputs

September 13, 2026. The [project audit](aurora-translator-project-audit-2026-09-13.md)
records ten reproduced discrepancies in option membership, explicit reference
handling, direct-choice accounting, primary support marking and publisher flags.
The static generic option index and runtime loader now share complete support
expression evaluation across packages. This extends section 15's earlier boundary,
which had only removed implicit support/name matching from the option index.

Missing grant, extract and inline item IDs no longer resolve through name guesses;
stored choices cannot replace an explicit Aurora ID with a name or recycled row ID.
Nine malformed content grant references are now exposed for explicit authoring
correction and are listed in the audit. Installed XML was not edited. Three
Warlock example fixtures had a nonexistent Fiend ID masked by that fallback; they
now use the actual fixture definition ID without changing the expected behavior.
Scoped package refresh also preserves valid grants into unchanged packages;
full/scoped parity checks now include parent candidates and selector diagnostics.

All **51 console regressions passed**, including ten new audit regressions and
the existing correction lifecycle coverage. `git diff --check` passed. The audit
records the commands and captured verification log.

The correction preparation/acceptance lifecycle remains unchanged. See the audit
for verification, remaining dynamic/multiclass review boundaries, and temporary
database evidence. No production refresh, snapshot, commit or push was performed.

## 17. Runtime selection follow-ups and protected grant-repair drafts

September 13, 2026. The [follow-up report](aurora-translator-followups-2026-09-13.md)
supersedes section 16's dynamic-selector and implicit spell-list lookup boundaries.
Runtime support pools now preserve complete boolean expressions, character macro
bindings and unknown values under negation. Spell lists retain nested predicates;
slot caps use active owners. Direct selections satisfy choices only through the
actual option pool.

Implicit spellcasting follows active grants and validated applied-choice
provenance, rather than installed grant/selector row order. Ambiguous active
classes or profiles produce diagnostics and leave the list binding unresolved.
Legacy direct selections lacking ownership evidence may require explicit
choice/profile information; no name or package-rank fallback was restored.

Eight grant-reference repairs have extracted regression fixtures and full-file,
review-pending XML drafts under `artifacts/grant-reference-review`. These reuse
the existing correction contract and XMLHelper repair vocabulary. Complete
embedded baselines, fingerprints and source/output hash evidence are retained.
The subsequent user clarification preserves both POTA and XGTE Erupting Earth
definitions because their Aurora IDs differ. Xanathar's preference applied only
if they shared an ID. Stoneheart's third, nonexistent grant ID remains an explicit
unresolved diagnostic with both repair candidates; it is not silently aliased.
No draft was installed or accepted automatically, and no production database was
refreshed. All **58 console regressions passed**, including seven follow-up tests;
the build and `git diff --check` passed. The report records the commands and
outstanding boundaries. Changes remain local and uncommitted.

The distinct-ID clarification also removed the runtime spell-equivalence filter
that could hide one definition based on matching content and package precedence.
Different IDs remain independent choices, including under source restrictions.
The follow-up report records focused verification after the earlier 58-test run.

## 18. Stoneheart local correction installed

September 14, 2026. The user authorized resolving Stoneheart's missing
`ID_PHB_SPELL_ERUPTING_EARTH` reference to `ID_XGTE_SPELL_ERUPTING_EARTH`, assuming
this unresolved homebrew grant intended the Xanathar's first-party spell. This
supersedes section 17's unresolved-reference status without merging or removing
either existing Erupting Earth definition.

The protected local file was installed at
`custom/user/local/sorcerer-stoneheart.xml`. It preserves the full original
baseline, original declaration fingerprint and all other gameplay content, and
records the assumption and pending KibblesTasty confirmation in its reason. Its
single `replace` operation remains `review-pending`, not accepted upstream.

The updated protected-repair regression and an isolated CLI import of the actual
full file passed. The grant resolves to XGTE; both POTA and XGTE definitions remain.
SQLite integrity and foreign-key checks passed. Original and installed file
hashes were checked, and the original source was unchanged. See the
[correction record](stoneheart-local-correction-2026-09-14.md) for exact edits,
hashes and evidence. Only this local XML was installed; the other eight drafts
were not. No production database refresh, publisher contact, commit, push or
binary publication was performed.

## 19. Production refresh blocked; existing records audited

September 14, 2026. The user authorized a production refresh and an audit, with
new findings to be discussed before further fixes. Lights' configured working
database is `custom/aurora-elements.sqlite`, with no additional content roots.
A consistent SQLite backup was captured before a full import was attempted on
a copy. The working database and all 1,190 installed XML files remain unchanged
by SHA-256; no activation, acceptance, retirement or binary publication occurred.

The existing unsupported-append guard in section 11 blocked preparation at
`core/dungeon-masters-guide-2024/items-firearms-and-explosives.xml`. The installed
corpus contains 1,215 active append operations across 55 files. Existing database
records omit concrete effects, including DMG firearm grants and Acid Splash's
appended `UA Artificer` support. Append-capable preparation is required before
this route can refresh production; removing the guard would discard content.

After applying the existing correction evaluator in memory, three unmarked local
files also contain 78 nonmatching same-ID declarations: Farmer (1), PHB 2024
equipment packs (31), and Eberron Artificer (46). These need reviewed correction
intent rather than an inferred winner. All seven managed local files remain
protected and non-retirable. Stoneheart's local correction is ready for import,
but the unchanged production database still contains its old POTA guess.

A separate derived-relationship rehearsal preserves raw content, correction
mirrors, package settings and all 40 Blazing Dawn infusion memberships. Both
Staff of Flowers item IDs and both real Erupting Earth spell IDs survive.
Devout binds to the repaired targets; Tatsumi remains stored but unavailable
under Ryoko's existing disabled setting. Nine old guessed grant links become
unresolved, as intended. This copy was not activated and is not a full XML import.

Diagnostic follow-ups: 462 supposedly missing archetype parents have multiple
valid candidates, while 2,354 unresolved grants point to disabled installed
definitions (2,021 Essentials, 333 Ryoko's). Report these separately from absent
IDs. Eleven grants have absent definitions: the nine reviewed spell references
and two Ryoko's feature references. Existing source warnings also include 93
IDs authored in `name` but preserved exactly as target IDs, and three identical
within-file duplicate pairs; raw totals are not gameplay-failure counts.

**All 59 console regressions passed**, exit 0, with no build warnings/errors.
SQLite integrity/foreign-key checks passed on the preserved database and derived
copy; metadata/spellcasting integrity checks passed on the preserved database.
No tests were weakened. Successful append preparation and more precise
diagnostics require new focused coverage when implemented.

See the [production audit](production-refresh-audit-2026-09-14.md) for findings,
suggested next steps and verification boundaries. The rollback backup and
detailed evidence are under `artifacts/production-refresh-20260914`; the full
test log is `artifacts/production-refresh-tests-20260914.log`. No implementation
fixes, package-setting changes, installed-content edits, commit or push were
performed during this audit.

## 20. Append preparation, unrestricted catalog, and consumer handoff

September 14, 2026. The user authorized base/corrections-before-extension order,
exact-ID append targets, protected source-qualified aliases for conflicting local
definitions, and an unrestricted database with optional app-layer filtering.
The [implementation and verification record](append-preparation-2026-09-14.md)
documents the current Translator work and supersedes earlier sections that let
saved enable/disable preferences influence global catalog membership or links.

The source implements that preparation order, records append operations and all
suppliers, and validates persisted append effects before activation.
`PreparedCatalogReader` creates optional app projections from corrected base XML
without mutating global data. Source classification distinguishes package Source
declarations from entry citations and separates declared publishers in mixed
collections while preserving old preference values. Published non-Wizards
supplements remain third-party even when older metadata calls them homebrew.

All 65 regressions passed, followed by focused verification of the final published
source-classification refinement. Protected alias plans for 78 conflicting local
IDs (Farmer 1, PHB24 packs 31, EFA 46) were verified in an isolated copy. They remain
review-pending and uninstalled. The correction acceptance/download-evidence
boundary is unchanged: matching installed files do not authorize acceptance,
no files were retired, and verified-download evidence remains outside this slice.

The full isolated 1,190-file import and repeat import passed candidate validation
and the final record audit: 20,954 unique IDs, 1,194 applied appends and 21 retained
unresolved operations. Integrity and foreign-key checks passed; old preferences
are preserved and disabled packages do not remove definitions or links. Both
Staff of Flowers and Erupting Earth IDs survive, repaired grants resolve, and all
40 Blazing Dawn infusion memberships remain.

Validation found and prevented two forms of silent data loss. Per the user's
decision, unknown types `Action`, `Weapon Category` and `Feat Features` now remain
generic records with diagnostics. Spell rules and other shared Aurora content
use the existing element parser/writer, preserving Find Familiar's companion
selection and appended rules. No specialized meaning was invented for unknown
types. The report records nine absent grants, an incorrect authoritative Guard
pack crossbow reference, unbound append operations and their review boundaries.

All installed XML hashes and the working database are unchanged; production
refresh remains pending the consumer rollout. The successful rehearsal contains
its own input paths and must not be copied over the production database.

The user requires Aurora-Lights edits to be handled in its own task. The
[consumer handoff](proposals/reflections-prepared-content-v12.md) includes a staged
eight-file patch; nothing was applied or built in that checkout. Reflections
integration, Legacy verification, and coordinated v12 rollout remain pending.
No commit, push, snapshot, app bundle, installed-content edit, or production
activation was performed in this follow-up.

## 21. Lights interim consumer integration and required shared writer

September 14, 2026. The user clarified that the app ships with essentials only:
users supply content and must build their own SQLite database on the device.
Loading a database built elsewhere is not a substitute for this workflow. The
interim Windows executable integration is accepted, provided the near-term shared
library migration includes the complete preparation and database-writing pipeline.
Android and Catalyst first-run database creation remains required work. Extracting
only the reader/composer does not satisfy that requirement. Isolate CLI/SQL Server
dependencies, then verify native SQLite, storage access, cancellation and memory
behavior on each target.

The Lights worktree now integrates the prepared consumer with changes beyond the
original proposal. Primary content is imported through the updated bundled Windows
Translator, with real paths and without Lights' old correction-staging wrapper.
Until a CLI capability command exists, Lights probes the executable on disposable
XML and verifies the output preparation marker and input hashes before giving it
user content. Translator remains responsible for candidate validation, activation
and correction retirement. The local bundle was rebuilt; no release was published.

Secondary configured folders remain available through raw XML, without forcing
them into the primary database. Runtime XML and current local corrections replace
the appropriate prepared base files before database/runtime append operations are
replayed together once in file order. Overlapping roots are deduplicated. Conflicting
same-ID definitions require explicit correction intent or separate identities.
Primary database freshness and secondary XML loading are separate. A valid runtime
hotfix can apply before import; this is not correction acceptance or retirement.

Loading/postprocessing uses a candidate collection and isolated lookup state;
failed reconstruction preserves the previous working collection. Snapshot/parity
reads do not publish live lookup maps. Prepared parity uses the app's projection
policy and requires current primary input hashes. Unknown authored types retain
shared content and produce generic diagnostics. The prepared loader does not expose
a singular archetype parent guessed from unrestricted database rows.

The copied reader now contains Lights-specific runtime-file composition and marker
protection changes; do not overwrite it blindly during extraction. Its provenance
is documented in Aurora-Lights/Aurora.Importer/PreparedContent/README.md. The detailed
integration plan and remaining checks are in Aurora-Lights/docs/prepared-content-v12-integration-plan.md.

Verification: 11 projection tests pass, including the actual bundled writer creating
a fresh database from user XML, secondary XML, real intrinsic resources, isolated
postprocessing, runtime corrections, append/filter isolation and future-contract
rejection. The 25 selected existing correction-lifecycle/database-recovery tests also
passed. Windows builds passed. These tests do not establish Android/Catalyst runtime
support, full installed-corpus parity, or an interactive UI rehearsal.

Production XML/database were not refreshed or altered. The alias drafts remain
uninstalled and are still required to resolve the current full-corpus conflicts.
No commit, push or public snapshot was made in this Lights integration turn.

## 22. Lights full-corpus rehearsal and alias draft follow-ups

September 15, 2026. Lights completed the requested disposable full-corpus and
v10/v11 compatibility rehearsal. Installed XML/database hashes remain unchanged;
no alias drafts were installed, and no production refresh or publication occurred.
Detailed results and the reproducible headless harness are in Aurora-Lights at
docs/content-database-rehearsal-2026-09-15.md and tools/ContentDatabaseRehearsal.
Raw evidence is under buildtmp/content-rehearsal-20260914-235248-c2e091 there.

The actual Windows app services successfully load v11 and a legacy-writer v10
fixture, create fresh v12, and migrate both old versions to v12. Fresh/migrated
prepared base/effective definitions, append operations and global catalog IDs
match. All v12 cases contain 20,954 distinct canonical IDs, 1,194 applied appends,
21 retained unresolved operations and 90 review-pending corrections. SQLite
integrity and foreign-key checks pass. Disabled preferences survive migration,
and 1,068 definitions from disabled packages remain stored. Enabled XML/database
projections match all 20,134 complete parsed definitions, including built-ins.
Thirty-seven focused tests pass; the Windows app and harness builds pass. This is
headless service evidence, not character/UI or Android/Catalyst runtime proof.

The three existing draft files rename 78 local-variant IDs (Farmer 1, PHB24 packs
31, EFA 46); their 79 add corrections also include Farmer's already-unique Tough
feature. They are separate local identities, not runtime alias redirections.
Saved character references are not automatically migrated by these drafts.

Do not install the EFA draft unchanged: its nested multiclass declaration still
uses ID_EFA_MULTICLASS_ARTIFICER, shared with the authoritative class. Extend the
draft/helper to assign a distinct local multiclass ID and rewrite related exact
requirements/references, preserving the embedded authoritative baseline. This
nested generated identity is outside the canonical elements table. It is the
only newly duplicated runtime ID compared with the v11 rehearsal; 462 other v12
runtime duplicate-ID groups already existed in v11, largely among generated
scroll/proxy identities. Canonical SQL uniqueness does not establish uniqueness
for those generated objects. No draft changes were made during this rehearsal.

A Lights runtime-composition bug was fixed: new local add definitions now retain
the local supplier, matching ContentPreparation.Evaluate, while replacements
retain the authoritative supplier. Otherwise a local variant inherited the
source preference of the official file. A focused regression verifies independent
supplier filtering. Preserve this behavior in the shared-library extraction.
Generic diagnostics also now skip intrinsic resources rather than labeling the
built-in Weapon Category as unknown authored content.

Observed timings: fresh v12 build 23.2 seconds; v11 upgrade 188.0 seconds; v10
upgrade 169.6 seconds; unchanged refresh 25.0 seconds; unchanged scan 0.276 seconds.
The saved-preference v12 load peaks at 848 MiB versus 579 MiB for v11, with load
times 21.0 and 19.0 seconds respectively. These are single-run observations, not
controlled benchmarks. Profile retained XML/projection memory before mobile
release; no particular allocation source has yet been proved responsible.
Refresh parent-process metrics exclude the Translator child and do not measure
writer peak memory. The all-enabled fresh-v12 load peaks at 904 MiB.

Minor producer metadata follow-up: content_root_hash is null on fresh v12 and
retains legacy values after migration. Input freshness uses local_correction_inputs
and passes the unchanged scan; clarify or maintain the older field for diagnostics.

Remaining work: repair/rehearse the nested EFA identity before draft installation,
interactive character/source/choice and progress/failure checks, memory profiling,
and the complete shared preparation/writer pipeline for on-device Android and
Catalyst creation. Rehearsal databases contain disposable input paths and must not
be copied over production. The shared library must carry full writer behavior,
not merely reader compatibility.

### September 15 follow-up: reconsider EFA alias intent; memory now measured

The preceding EFA recommendation needs qualification before implementation.
Installed user/local/efa-class.xml declares 0.1.4 versus authoritative 0.1.5, with
the same update URL and the same 58 top-level IDs. Normalized comparison finds
44 changed definitions: 41 description blocks, eight sheet blocks, one rules
block and one setter block (overlapping categories). The only rules difference
is the local subclass selector's extra level=3; the setter difference is a garbled
dash for Steel Defender challenge. Much of the prose is condensed, with some
omitted details. The local file already contains the class/feat/epic-boon changes
in content commit 553cfd5 (July 27), but not its subclass-selector correction.

This looks like an older working override, not evidence of an intended alternate
Artificer. The original creation intent is not proved by the files. Before fixing
and installing the alias draft, review whether this local override should instead
be retired or reduced to specifically justified corrections. Do not infer that
differing content requires a permanent second class. No installed content or draft
was modified in this follow-up. See the updated Lights rehearsal report for details.

The v12 fallback index now has an isolated measured retained cost: approximately
175.8 MiB for 20,134 definitions. Independent projection-only and full-load
experiments agree. Full-load managed retention drops from about 674.8 to 499.0
MiB when both the fallback snapshot and its publication callback are released.
Invalidating the snapshot alone does not free it: the lookup state's callback
still captures it. These are managed heap measurements, not peak working set.

Optimize the consumer by indexing existing parsed definitions with appropriate
mutation isolation, clearing the publication callback after successful activation,
and reducing repeated XML parsing/serialization. Preserve source filtering,
complete-definition fidelity, runtime corrections and rollback. Some additional
full-definition storage is intentional; the full peak delta is not attributed
solely to this index. Only the diagnostic harness/docs changed in this follow-up;
production memory optimizations remain to be implemented and verified.

## 23. Duplicate fallback memory fixed; stale Artificer override archived

September 15, 2026. The user approved fixing duplicate fallback storage and treating
the local Artificer as an old hotfix to archive. Lights moved the installed
custom/user/local/efa-class.xml to the sibling Content Archive directory at
2026-09-15-stale-artificer/efa-class.xml.archived. Its bytes are unchanged (SHA-256
4EEDAA7D481DAD269F696E576BA1743D60F15BCD06132049735E5905FAD0E11C). An installed-file
hash audit confirms this is the only XML change and the production database is
unchanged. The EFA alias draft is superseded: do not install it or automatically
regenerate the variant. Farmer/PHB24 pack drafts remain uninstalled and unchanged.

The consumer fallback index now shares the immutable serialized XML already held
by parsed gameplay elements, with small lookup metadata. It creates a temporary
independent XmlDocument only for a requested fallback operation. Do not share a
mutable gameplay node with the cache or retain another full DOM. The prepared
lazy-load path also retains XML strings. Legacy XML append assembly keeps its
existing mutable-document behavior. Materialization reuses one temporary node
through repair/parsing. The loader builds spell-access maps from parsed nodes
and clears the publication callback after activation, preserving staged loading
and rollback. No forced collections were added to production code.

Same-corpus before/after measurements: peak working set 844.3 to 617.5 MiB;
retained managed heap 674.8 to 503.1 MiB; fallback index 175.8 to 4.3 MiB. The
publication callback is no longer retained. These are single-run observations
and distinct memory measures; broader mobile profiling remains necessary.

Nine linked-service checks pass for list/equipment lookup, mutation isolation,
staging/activation/rollback, provenance, repeated materialization and exclusion.
All 37 selected projection/correction/recovery tests and the Windows build pass.
A disposable refresh/load/parity case without the EFA draft contains 20,908 unique
canonical IDs, keeps the authoritative Artificer, and removes every _LOCAL_EFA ID.
SQLite integrity/FKs pass. XML/database projections match all 20,088 definitions;
the EFA multiclass duplicate is gone after postprocessing. The 462 pre-existing
generated duplicate-ID groups remain outside this fix's scope.

Detailed evidence: Aurora-Lights/docs/content-database-rehearsal-2026-09-15.md and
buildtmp/content-rehearsal-20260914-235248-c2e091 (migration-v12 before/after,
archived-efa-v12, installed-archive-audit.json). Future prepare.py rehearsals can
explicitly exclude the retired draft using --exclude-draft efa-class.xml.
Production refresh, UI/character rehearsal, commit and publication remain pending.

## 24. Pre-UI validation: terminology, characters, reloads and packaging

September 15, 2026. The user clarified terminology: a local XML file is not an
override merely because it lives under user/local. Local files can add independent
content. Use override only for actual replacement behavior; embedded correction
metadata expresses managed correction intent. Same-ID differences do not prove
that the user wants permanent alternate identities. Do not automatically apply
the older Farmer/equipment-pack alias drafts on that assumption.

Installed pack comparison: user/local/players-handbook-2024-items-packs.xml and
core/players-handbook-2024/items-packs.xml both contain the same 36 IDs and declare
0.0.1. Neither has correction metadata. Thirty description and thirty setter
blocks differ largely in encoding; the local copy is not uniformly cleaner. Its
one extract-rule change fixes Guard's crossbow from
ID_WOTC_PHB24_WEAPON_CROSSBOW_LIGHT to the actually defined
ID_WOTC_PHB24_WEAPON_LIGHT_CROSSBOW. There are no new local pack IDs.

Farmer's local copy retains ID_WOTC_PHB24_BACKGROUND_FARMER but replaces its direct
Tough grant with ID_WOTC_PHB24_BACKGROUND_FEATURE_FARMER_TOUGH, a new Background
Feature named Origin Feat (Tough). That feature grants Tough plus
ID_INTERNAL_GRANTS_BACKGROUND_WITH_A_FEAT and adds a sheet/display entry. Other
Farmer grants are unchanged. Both copies declare 0.0.1 and neither has correction
metadata. No Farmer or pack file was modified or archived in this follow-up.

The requested automated checks are recorded in Aurora-Lights/docs/pre-ui-validation-2026-09-15.md.
Four full-corpus reloads retain 502.02, 508.05, 508.05 and 508.06 MiB after diagnostic
collection. Thirteen actual app-service checks pass for fresh creation, secondary
XML updates without import, supplier enable/disable, retained canonical rows,
malformed-correction reconstruction rollback, rejected refresh preservation, and
cancellation at main-import start with intact database and Idle state. This is not
power-loss verification at every activation boundary.

Character evidence reveals a new compatibility gap: saved preferences disable
core-ale-xml (Aurora Legacy Essentials). v11 supplied built-in copies anyway; v12's
no-resurrection rule excludes intrinsic alignment/language/proficiency/vision IDs
when their catalog supplier is disabled. Art E reports 44 unset items and Fresh E
90 with these settings, versus v11 zero/three. Enabling only Essentials in a third
disposable v12 database restores nearly all registrations; generated Claw remains
a difference. Gobric, prepared-paladin and Remy Morningstar each pass isolated
load/save/reopen under v11 and Essentials-enabled v12, but initially report 68,
76 and 100 unset items with the saved v12 preferences. Their within-run ID/spell,
choice-row and inventory round-trips are stable. Other cross-version differences
include generated proficiencies from disabled Ryoko sources, which v11 included.

The harness uses actual app selection/spell handlers and the shared character
reader/writer, but not CharacterService/BuildService extras reapplication and UI
normalization. Art E loses three ASI registrations on round-trip in both v11 and
v12; sequential multi-character runs also stalled after two characters. These
existing shared-reader/lifecycle findings require tracing through the app wrapper
before assigning them to an equivalent UI failure. They were not counted as
passing or hidden by saving already-reduced characters. Original user character
hashes, XML and production database remain unchanged (apart from the previously
authorized Artificer archive, which was not altered further).

Proposed policy decision, not implemented: keep a narrowly defined intrinsic
Essentials provider available independently of selectable published sources while
continuing to exclude ordinary disabled content. Do not blanket-resurrect all
excluded IDs. This needs an explicit host/supplier contract and consumer tests.

A win-x64 Release publish succeeds; the exact bundled executable creates a fresh
prepared v12 database from user XML with development paths removed from PATH.
However, Windows CI/release workflows do not fetch/build the Git-ignored Translator
bundle, so clean-CI packaging is not reproducible. Also, the current writer bundle
is framework-dependent: an empty-runtime simulation fails with a missing-.NET
error even though the app release workflow is self-contained. Recommend a pinned,
compatible self-contained producer artifact for the interim Windows release,
then the shared library. No producer artifact/version was invented or selected,
and no workflow/dependency policy was changed. Windows ARM64 and clean-VM/installer
execution were not verified. No commit, push or public release occurred.

## 25. Required infrastructure, confirmed local intent, and character compatibility (2026-09-15)

This supersedes section 24's pending Essentials policy and provisional pack/Farmer
alias interpretation. The user approved mandatory Essentials/Internal/Core
infrastructure, explicitly leaving PHB, DMG, and Monster Manual selectable even
though their package kind is also `core`.

Lights now implements `RequiredContentPolicy` in Builder.Data. It recognizes
source labels `Internal`, `Core`, `Aurora Essentials`, and `Aurora Legacy
Essentials` (case-insensitively), and known supplier keys `core-ale-xml`,
`core-internal-xml`, `core:internal`, `core:core`,
`core:aurora-legacy-essentials`, `core:aurora-essentials`, `runtime-builtins`.
Broad `package_kind=core` is not an always-enabled flag. Consumer projection and
package-list reads ignore stale disabled flags for these required suppliers;
Settings locks their switches and the toggle API rejects attempts to disable
them. Shared character source restrictions likewise protect required source
identities while keeping rulebooks selectable. No producer filtering was added.
Keep the catalog unrestricted; retain this policy at the consumer boundary when
the reader/importer becomes a shared library. New supplier identities for this
infrastructure need explicit policy alignment rather than reliance on kind alone.

Actual saved preference: row 1, `core-ale-xml`, `ALE.xml`, `core`, `is_enabled=0`.
The separate `core:aurora-legacy-essentials` row was enabled, but the physical ALE
source file belonged to the disabled supplier. Its disabling action/time is
unknown. The production database is unchanged; effective runtime policy now
overrides that stale flag without requiring a database refresh or rewriting it
during reads.

Both installed PHB 2024 pack files define the same 36 IDs: 16 background bundles,
13 class bundles, and seven shop packs. Those two categories are distinct; the
duplicate declarations are across the full core/local file copies. No pack IDs
were renamed or deleted. The local Guard entry's corrected Light Crossbow
reference is now explicitly annotated using embedded correction metadata:

- Local file: `user/local/players-handbook-2024-items-packs.xml`.
- Source: `core/players-handbook-2024/items-packs.xml`.
- Operation/key: `replace` / `guard-light-crossbow-reference`.
- Target: `ID_WOTC_PHB24_ITEM_BACKGROUND_EQUIPMENT_PACK_GUARD`.
- Correct weapon: `ID_WOTC_PHB24_WEAPON_LIGHT_CROSSBOW`.
- State: `review-pending`; authoritative baseline and original fingerprint
  embedded. All existing local XML payload is unchanged by annotation.
- New SHA256: `2DD8407C1D3E155F2FD0B572308B7D0DEB24F245BD1B93BCADB6C51999236BE1`.

Unclassified local text/encoding edits remain preserved; they can still prevent
whole-file retirement. Do not blindly apply the old `_LOCAL_PHB24_PACKS` alias
draft. Its input hash is now stale. Farmer's local feature wrapper and extra
background-with-feat grant are an intentional preference, not an accidental
duplicate. Farmer remains unchanged; do not install the earlier Farmer alias
draft as though the user had requested a separate variant. Its eventual explicit
replacement/preference representation must preserve the user's chosen behavior.

Validation: 12 new policy cases; 26 passing tests across required policy,
source-editor, and prepared-projection checks; successful Windows app build.
Three direct Guard annotation checks passed. Installed-file audit confirms only
the previous Artificer archive and new pack annotation differ from the original
snapshot; Farmer and the production database are unchanged.

Character compatibility is a separate Lights gap. Art E's existing racial ASI
selection has a registered value but no active progression manager because its
rule requires `!ID_INTERNAL_GRANTS_BACKGROUND_ASI` and that background grant is
present. The shared writer omits the inactive rule subtree while retaining its
three IDs in the summary, causing save/reopen to lose the +2 Intelligence/+1
Constitution subtree. This reproduced with v11 previously and with v12 now;
the definitions themselves exist. Recommended, not implemented: verify the
app-wrapper normalization path, preserve unresolved historical choices without
activating ineligible bonuses, and require explicit resolution when an equivalent
legal mapping into current choices cannot be proven. Do not solve this by
loosening catalog uniqueness, restoring excluded rulebooks, or double-applying
racial and background bonuses.

A new instrumented three-character sequential run with each save/reopen completed
in 75.64 seconds, with no lifecycle-code fix. The earlier stall is not reproduced
or explained; it must not be claimed fixed. Art E still fails roundtrip and Fresh
E still reports six initial unset elements. App-wrapper tracing remains needed.
The new run used disposable earlier Farmer/pack drafts, not a full rehearsal of
the newly annotated installed corpus.

Packaging remains a proposal: a pinned self-contained Translator artifact,
explicitly acquired in Windows CI/release, until the shared library replaces the
child process. No artifact version or workflow change was selected. No commit,
push, release, or production refresh was performed in this follow-up.

## 26. Background ASI authority and final pre-UI content prerequisite (2026-09-15)

The user confirmed that PHB 2024 background ASIs should be authoritative and the
builder must not allow the inactive racial ASI chain alongside them. This
supersedes section 25's tentative preservation/manual-resolution plan for this
particular ineligible ASI subtree.

Lights now shares `AbilityScoreSelectionCleanup.Normalize` between CharacterFile
load validation and the app's existing stale-ASI cleanup. After the whole
character is registered, it removes selected ASIs whose originating choice is
inactive, clears their slot registration, and repeats so nested selections are
removed too. Eligibility comes from the current content rules and active
progression managers; the implementation does not key on character names or
unconditionally delete racial ASIs. Legal racial choices without a background
ASI remain intact. Saved-count adjustment discounts only ID occurrences actually
removed by cleanup; unrelated missing content is not discounted. No catalog,
append contract, source precedence, or uniqueness change is involved.

Validation: the earlier Art E sample now passes load/save/reopen with stable
registered IDs, all six additional ability scores, choices, and inventory. Nine
runtime checks cover background authority, full racial-subtree removal,
idempotence, legal racial choices without the background, and background
restoration. Three focused saved-count tests passed. Windows app build passed.
The background's actual ASI option was unselected and remains for the user to
choose; no automatic reassignment of the old Intelligence/Constitution bonuses
was made. All edits to the character in tests were in memory or disposable
roundtrip copies; original characters remain unchanged.

The remaining content prerequisite is now verified: an isolated refresh with
the actual installed Guard annotation and intentional Farmer file fails on
`duplicate-element-id: ID_WOTC_PHB24_BACKGROUND_FARMER`. Propose annotating Farmer
as an intentional replacement of the authoritative definition, retaining its
ID, wrapper, and added background-with-feat grant, then rerunning the isolated
refresh. Do not install the earlier Farmer alias draft. No Farmer annotation or
production refresh was performed in this follow-up.

The historical sequential stall still needs UI switching coverage; the latest
instrumented sequential run completed and no causal fix is claimed. Fresh E's
previous six-unset-element finding is separate from the ASI fix. Reproducible
self-contained Translator packaging remains a release prerequisite, not a
blocker for a local worktree UI review. No commit or release was made.

## 27. Required source controls in Settings (2026-09-24)

Aurora Lights now exposes infrastructure under **Required builder sources** in
both Settings default source restrictions and the shared character source editor.
Every required row is checked, disabled, and labeled **Always enabled**; the group
itself cannot be disabled. Source items and groups enforce this in the model, so
old saved restrictions, direct calls, and bulk toggles cannot turn them off.
Only actual optional, unchecked sources are persisted as default restrictions.

The host policy recognizes the source names Internal, Core, Aurora Essentials,
and Aurora Legacy Essentials, ignoring case and surrounding whitespace. The
stable Source ID ID_SOURCE_AURORA_LEGACY_ESSENTIALS also remains protected if its
name changes. Internal/Core labels present in the loaded catalog are displayed
even without separate Source declarations. PHB, DMG, Monster Manual and other
ordinary rulebooks remain selectable, regardless of a Core label/category on
their Source declaration. The audit of 210 installed Source declarations found
Aurora Legacy Essentials was the only declaration explicitly requiring builder
infrastructure protection; no new publisher or rulebook category was locked.

This is host-side source eligibility and UI behavior, not importer filtering:
the database still contains all eligible content. No database refresh is needed
for this change, and the shared library should not drop definitions based on
these character/default restrictions. RequiredContentPolicy for these controls
now lives in Aurora.Logic/Services/Sources (the earlier Builder.Data location in
section 25 is historical).

Validation: 38 focused tests passed across required-source policy, rendered
source controls, default restriction fallback, source-preference migration, and
category classification. No live content, character files, or app settings were
modified for this validation. Rebuild/restart the app for the updated controls.

## 28. Import progress presentation (2026-09-24)

The pinned Aurora.Content 0.7.0 library already emits intermediate Writing
callbacks. An isolated 2,000-element import through ContentDatabaseService.SyncAsync
now verifies that real write counts and changing percentages reach StateChanged
while SyncState is still Syncing, before the completion callback.

Lights previously displayed the same changed-file summary throughout comparing
and writing. Settings now distinguishes scanning, reading, comparing and writing,
shows completed/total files or elements and the current file, and keeps an animated
activity indicator alongside measured progress. Resolving and Activating have no
measurable totals, so the host displays an indeterminate bar and distinct labels
rather than leaving a static 90-percent bar. Percentage remains a weighted phase
share, never an elapsed-time estimate. Long synchronous work between library
callbacks is not reported as completed by a synthetic timer.

Settings no longer performs correction, skipped-content or metadata SQLite reads
inside its render path. Diagnostics are read off the UI thread on initialization
and after refresh/state changes, retaining the previous results during import.
This avoids repeating synchronous database queries for each progress update.
No importer, database contract, package pin, or activation policy change is needed.

Validation: all 22 focused progress tests passed, including the isolated real
sync, and the Windows Release app build passed with zero warnings or errors.
Installed-app animation during a full user-content refresh remains a visual
check; this change does not claim reduced importer execution time. Live content,
character saves and the installed database were not modified for validation.
