# Shared content 0.11.1 closeout

November 4, 2026. This patch includes the original-input fingerprint fix from
`cea45d8`, preparation-aware integrity diagnostics, and final all-declaration
metadata counts. Both shared packages advance together from 0.11.0 to 0.11.1.
No public API or correction acceptance policy changes. Schema/data remain
**1/19**, and preparation contract remains **2**.

## Behavior and compatibility

Integrity inspection compares original input bytes with captured hashes, then
compares normalized identities, declaration status and spellcasting with the
stored finalized declarations. It accepts the intentional differences caused by
corrections, append composition, superseded definitions and retired files, while
continuing to report changed inputs and inconsistent stored projections.
Import review/recovery diagnostics remain visible separately. This does not
replay preparation or prove independent parity with Legacy.

Provenance writing now records the element count after adding superseded rows,
inside the existing transaction. Existing data-19 databases remain readable;
ones with the old stale count need a normal `ContentImport.ImportAsync` refresh
to repair that value. Merely installing the patch does not rewrite a database.
Older data versions retain their existing refresh requirements. Source-file
verification expects the original captured root; relocating it requires refresh.

## Verification

The Release build succeeded and the full executable suite passed **166 tests,
0 failures, exit 0**. The previous focused pass verified 16 cases, including
regression failures before the fix and non-mutating integrity checks. The
pre-commit review found no additional blocker. A separate consumer restored the
actual packages into an isolated cache using only `PackageReference`, with no
project references or friend-assembly access. Its Release build had zero warnings
and errors; **20 runtime checks passed, 0 failures, exit 0**.

Package checks cover assembly/source versions, Contracts access, embedded schema,
catalog queries and aliases, provisional conflicts, source classification, append
provenance, non-mutating reads, correction review/editing, stale-review refusal,
and import-gated retirement. They also verify final metadata counts on a fresh
import and normal refresh of a simulated old data-19 count mismatch, preserving
the catalog entries. This is fixture verification, not a production refresh.

```powershell
dotnet build AuroraTranslator.Tests/AuroraTranslator.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
./AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe
```

The build has the existing two CS8632 warnings and two NU1900 warnings for the
unavailable vulnerability feed. No vulnerability-feed verification is claimed.
Logs are under `artifacts/release-verification/0.11.1/`.
Tests used disposable XML/SQLite fixtures and the repository regression snapshot.
Candidate activation required the same sandbox exception established during the
focused pass; installed content and the production database were not inputs.

## Immutable packages

Both packages were built from clean source commit
`2262fec4cac103990883b29460f43c9e9416fbb9`. Both assemblies report
`0.11.1+2262fec4cac103990883b29460f43c9e9416fbb9`, and both NuGet repository
records identify that commit. Content depends on Contracts 0.11.1 and
Microsoft.Data.Sqlite 10.0.12. The later documentation-only commit records this
verification; it is not the packages' source revision.

| Local artifact | SHA-256 |
| --- | --- |
| `Aurora.Content.Contracts.0.11.1.nupkg` | `468b36943ffe217c49b4c3104a02a39d8911d4779783c9e9718c0226eb8d282d` |
| `Aurora.Content.0.11.1.nupkg` | `891c252a4c939dad78add3cbedd9fffa287ed068594fe45111a5c19a7f80ff49` |

Packages and `manifest.json` are under `artifacts/packages/0.11.1/`. No existing
package was overwritten. Package creation had the existing missing-readme
warnings. Offline consumer restore used the local artifacts and cached dependencies
with NuGet audit disabled. Consumer source/evidence is under
`artifacts/package-smoke/0.11.1/` and `artifacts/release-verification/0.11.1/`.

```powershell
dotnet pack Aurora.Content.Contracts/Aurora.Content.Contracts.csproj -c Release --no-restore -o artifacts/packages/0.11.1 -p:UseSharedCompilation=false -p:RepositoryCommit=2262fec4cac103990883b29460f43c9e9416fbb9 -p:SourceRevisionId=2262fec4cac103990883b29460f43c9e9416fbb9
dotnet pack Aurora.Content/Aurora.Content.csproj -c Release --no-restore -o artifacts/packages/0.11.1 -p:UseSharedCompilation=false -p:RepositoryCommit=2262fec4cac103990883b29460f43c9e9416fbb9 -p:SourceRevisionId=2262fec4cac103990883b29460f43c9e9416fbb9
```

These record provenance; do not repack an existing immutable version. Consumers
should vendor both packages together with their hashes and clean source revision.

## Release boundaries

This release is committed and packaged locally. No remote push, tag, NuGet-feed
publication, consumer repository change, installed XML edit, or production
database refresh was performed.
Consumer adoption and its own regression checks belong in the Aurora-Lights
context. The supported core is ready for maintenance within its documented scope;
multi-root orchestration, cross-file review, publisher-download evidence, broader
evaluator coverage and a Translator desktop UI remain separate future work.
