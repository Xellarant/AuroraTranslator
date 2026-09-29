# Catalog summary and detail API

`Aurora.Content.ContentCatalogReader` is a small synchronous reader for an imported
SQLite catalog. It centralizes effective-definition queries for future Compendium
consumers without defining a UI, character eligibility, or source preferences.
The implementation uses the same resolved-winner tables already queried by Lights;
it does not run another correction evaluator or choose a new definition.

```csharp
using Aurora.Content;

ContentCatalogSnapshot catalog = ContentCatalogReader.ReadSummaries(databasePath);
var rituals = catalog.Entries.Where(e => e.Spell?.IsRitual == true);
ContentCatalogDetail? detail = ContentCatalogReader.ReadDetail(databasePath, auroraId);
```

## Summary contract

- `Entries` contains every effective Aurora ID, ordered by ordinal ID. Same-name
  definitions with different IDs remain separate. Superseded declarations,
  unavailable IDs, and alias addresses are not additional entries.
- Identity, name, type, cited source book, `CompendiumDisplay`, imported summary
  text, selected supplier, and basic spell facets are returned. `SummaryText`
  falls back to the first description when no summary exists; it is not truncated,
  formatted for display, or sanitized HTML. Spell facets expose stored level,
  school (currently normalized by the importer, e.g. `divination`), ritual and
  concentration flags. Other type-specific facets remain future additions.
- Hidden and internal types are included. The host decides what its Compendium
  displays, including whether to show spells whose display flag is false. Search
  folding, sorting by name, excerpts, and all user filters remain app concerns.
- `Supplier` describes the selected element row's source file/package. Its
  `PackageKind` preserves the importer vocabulary: `core`, `official`,
  `third-party`, `homebrew`, or `local`. UA is first party (`official`); explicit
  local/homebrew files are `homebrew`; published non-Wizards supplements are
  `third-party`. The legacy `local` value can indicate unknown or conflicting
  publisher metadata; it must not be relabeled as known homebrew. Classification
  describes the supplier, separately from the element's cited `SourceBook`.
- `ResolutionKind` carries the recorded decision, when present (`provisional`,
  `retained`, `upstream-successor`). `SupersededDeclarationCount` shows how many
  rejected declaration rows remain for inspection. Neither property silently
  accepts a conflict; the existing `ContentDatabaseReader.ReadSkippedContent`
  and health APIs provide review diagnostics. A superseded row is not necessarily
  an unresolved conflict: an established upstream successor can also supersede it.
- Saved package enable flags and precedence preferences do not filter this read.
  Selection comes from the prepared database's effective definition, not from a
  ranking recomputed by the catalog reader.

## Detail and explicit references

`ReadDetail` accepts an exact, case-sensitive Aurora ID. It first looks for that
identity, then a stored alias only if the original ID is absent. It does not trim
or normalize the caller's identity. Unknown or unavailable IDs return `null`.
The result retains `RequestedId`, the effective summary's `AuroraId`, and alias
origin/note so forwarding is visible. Alias chains are not traversed: preparation
already requires the stored target to be a live definition.

Detail adds the first description's text and Aurora XML markup, plus the prepared
`EffectiveXml` after correction evaluation and applied appends. This is the
imported effective content, not a claim that current disk XML still matches it.
`EffectiveXml` can be null for records without a prepared XML definition, such as
optional external JSON imports. Their relational summary/description can still be
read. Markup must pass the consuming application's rendering policy before display.

`Suppliers` credits all surviving base-definition suppliers. `AppliedAppends`
separately lists materialized append XML, its supplier classification and stored
zero-based ordinal, ordered by file path and ordinal. Rejected/skipped appends are
not represented as applied content. Correction review states and rejected
declarations remain available through the existing review/database surfaces.

`Links` covers direct `<rules><grant id="..." /></rules>` references and Companion
`traits`, `actions`, and `reactions` setter IDs. It resolves the explicit target ID
with the same alias rules, returning `Resolved`, `MissingTarget`, or `TypeMismatch`.
Mismatched targets remain inspectable; missing targets are not silently discarded.
Reference padding is trimmed. Link order follows grants in XML order, then companion
setters in XML/list order. Duplicates are retained rather than silently collapsed.

A resolved grant means only that its target identity exists. `RuleXml` retains
level, requirements, and other attributes; the reader does not decide whether a
character qualifies. Links are not expanded recursively. It does not infer
Information relationships from ID spelling, name matches, or source precedence.
Supports, selectors, nested/non-grant rules, parent heuristics and other relationships
remain in `EffectiveXml` and existing normalized database surfaces for later typed
API additions. This slice is not a replacement for the complete Lights Compendium
service or its runtime fallback.

## Snapshot and compatibility boundary

Each call opens an existing database and reads metadata and results in one SQLite
read transaction. Results are materialized before the connection closes. There is
no shared mutable cache. Separate calls can see different import generations;
hosts must invalidate their own catalog/detail caches on successful refresh and
protect asynchronous UI results from an older request. Returned metadata describes
that call's database; it is not a new cache-token protocol.

The reader requires the current schema/data versions (currently **1/17**) and the
unrestricted/materialized preparation contract. Older/newer or unprepared databases
fail with `InvalidDataException` and refresh guidance; SQLite access/query failures
retain the original exception as the inner exception. A missing file throws
`FileNotFoundException` without creating a database. An unknown ID in a compatible
database returns `null`, which is intentionally different from an unreadable catalog.

Normal reads do not mutate XML or SQLite. The existing shared connection helper can
recover an interrupted SQLite transaction when a rollback journal is present. The
API does not import, activate, repair content, or accept/retire corrections.
Freshness checks remain `ContentDatabaseReader.IsStale`; live XML overlays and
additional roots remain the responsibility of `PreparedCatalogReader` and the host.
No schema or data-version bump is needed for these read-only APIs.

## Fixtures and verification

`AuroraTranslator.Tests/Fixtures/CatalogReader` contains synthetic content for:
effective/superseded definitions; distinct same-name IDs; direct and alias lookups;
conditional grants; missing/wrong-type companion links; appends; co-suppliers;
hidden records; and core/UA/supplement/local classification. Tests also cover source
preferences, non-mutating reads, snapshot freshness boundaries, unchanged and changed
reimports versus fresh imports, and incompatible databases. Compatibility rejection
fixtures are not a migration rehearsal from an actual historical database.

The existing correction fixtures additionally verify both Staff of Flowers
definitions after the DMG rename and the corrected Devout/Tatsumi grant targets
through the public API.

Verified September 29, 2026: the Release solution build succeeded and the full
Release test executable passed **124 tests, 0 failures, exit code 0**, including
the five new catalog tests and expanded correction fixture. The build reported
the two existing CS8632 nullable-context warnings in `AuroraSqliteImporter` and
NU1900 because the NuGet vulnerability feed was unavailable. Commands:

```powershell
dotnet build AuroraTranslator.sln -c Release -m:1 -p:UseSharedCompilation=false
./AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe
```

The release target is **0.10.0**, with schema/data **1/17** unchanged. The existing
immutable **0.9.0** packages predate this API. Package from clean committed source
and verify the actual NuGet artifacts before consumer adoption. Full item/companion facets,
consumer UI integration, multi-root imports, and declaration-review APIs remain
separate follow-ups.
