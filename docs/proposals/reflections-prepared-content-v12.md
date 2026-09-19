# Proposed Aurora-Lights integration for Translator prepared content

Status: **proposal only**. No Aurora-Lights source files were changed. The patch
has not been compiled or runtime-tested in that project. Work belongs in the
Aurora-Lights task; do not deploy a new production database on the assumption
that this adapter is already installed.

The staged patch is [reflections-prepared-content-v12.patch](reflections-prepared-content-v12.patch).
It contains eight files with repository-relative paths for Aurora-Lights.
Original source hashes and the complete proposed files are also under
`5eApiTranslator/artifacts/reflections-prepared-adapter/manifest.json`.

## Prompt to hand to the Aurora-Lights task

Review the sibling Translator handoff and the staged patch at
`C:\Users\Ralla\source\repos\5eApiTranslator\docs\proposals\reflections-prepared-content-v12.patch`.
Implement the appropriate Aurora-Lights consumer changes in this task, preserving
unrelated work. Treat the patch as an uncompiled starting point, not a verified
drop-in implementation. Do not publish a snapshot or refresh the production
database without coordinating the rollout.

Translator now prepares base definitions and protected corrections before
applying exact-ID append operations. Its v12 catalog contains every source and
all materialized links independently of saved source preferences. UA is first
party, explicit local content is homebrew, and published non-Wizards supplements
are third party. Definitions with distinct IDs remain independent choices.

The proposed changes are:

- `Aurora.App/Services/DbElementLoader.cs`: recognize v12's capability marker;
  load complete prepared XML through the existing element parsers. Apply package
  preferences in the app projection. Use captured input hashes to reject a stale
  prepared snapshot and fall back to complete XML. Supply intrinsic resource
  definitions, including `ID_INTERNAL_GRANTS_CHARACTER_BASE`, for retained
  operations whose targets exist only in the host.
- `Aurora.App/Services/XmlContentFallbackService.cs`: use the same projection for
  fallback option queries. A v12 database load skips the old raw-local overlay
  and unsynced XML merge, preventing a second application of append effects.
- `Aurora.Importer/PreparedContent/`: stage the same portable `ContentText`,
  `ContentAppendComposer`, and `PreparedCatalogReader` sources used by Translator.
  No shared-library packaging migration is proposed in this slice.
- `Aurora.Importer/AuroraContentImporter.cs`: when toggling the stored package
  preference on a prepared catalog, do not rebuild its global cache or links.
  Storage of `is_enabled` remains transitional; it is app policy, not a catalog
  membership rule.
- `Aurora.Importer/AuroraSqliteImporter.cs` and
  `Aurora.App/Services/ContentDatabaseService.cs`: guard a prepared database from
  an older bundled importer that cannot preserve the preparation contract.
  Until a preparation-aware importer is integrated, show an actionable standalone
  Translator refresh instruction. Review this temporary UX in Aurora-Lights.

Legacy currently loads source XML and applies appends by exact ID. The proposal
does not change that path. Verify Legacy alongside Reflections, especially
intrinsic-resource appends, protected aliases, and current local corrections.

## Contract and verification to perform in Aurora-Lights

The new SQLite tables are `content_preparation_metadata`,
`content_prepared_elements`, `content_prepared_sources`, and
`content_append_operations`. Their views expose source/package provenance.
`local_correction_inputs` records the real XML path set and SHA-256 hashes.
The user also approved preserving unrecognized authored types as generic records.
`element_types.loader_family='generic-unrecognized'` and
`v_unrecognized_element_type_diagnostics` identify them. Current examples are
Ryoko's `Action`, `Weapon Category`, and plural `Feat Features`. Their exact IDs,
types and shared content remain stored; Translator does not infer specialized
gameplay semantics or silently change the plural type to singular. The app task
must review its generic parser/display handling and surface the diagnostics.
`base_xml` already includes corrections; `effective_xml` includes all appends.
A filtered runtime projection must start from **base_xml**, then apply only
eligible append operations once. Do not append onto effective XML or onto an
already merged runtime collection. Default unfiltered reads must retain all
content. Filtered calls must not mutate any database records.

Check these cases before rollout:

- UA Artificer spell supports and DMG firearm proficiency grants appear once.
- Filtering an extension's source removes its contribution from that app view;
  an unfiltered view still has it, and persisted links remain unchanged.
- Intrinsic resource appends bind by exact ID. A host definition cannot resurrect
  an intentionally filtered catalog element with the same ID.
- A stale or failed prepared load replaces no working runtime collection with a
  partial result. Inspect auxiliary maps and fallback caches as well as elements.
- Source changes invalidate cached projections; the old importer cannot silently
  rewrite v12 into a graph derived from enable/disable preferences.
- Both Staff of Flowers and Erupting Earth IDs survive, Devout/Tatsumi grants
  target the repaired IDs, and local variants remain distinguishable by ID/source.
- Review metadata maps against the filtered projection; do not reintroduce a
  guessed singular parent when several supports/grants give valid candidates.
- Build the current Reflections target and run focused consumer regressions.

Translator's focused tests exercise the shared reader/composer contract, including
filter isolation, replay, correction ordering and intrinsic host definitions.
They do **not** establish that this staged MAUI integration builds or that the
installed Reflections binary supports v12. The full-catalog rehearsal and remaining
Translator findings are tracked separately in the main data handoff.
