# Shared content 0.11.0 release verification

Verified October 6, 2026. Both packages advance from 0.10.1 to
**0.11.0** for the new public correction editor/review API and durable local
approval state. The [API contract](correction-editor-contract.md) documents the
workflow, provenance, same-file group policy and deliberate boundaries.

Schema stays **1**, preparation contract stays **2**, and data advances
**18 -> 19**. This is a semantic review-state guard, not a new SQL column:
`local_corrections.state` mirrors normalized state, and stored `local_xml` carries
the approval stamp. Existing databases require `ContentImport.ImportAsync`.
Older packages reject unknown `approved-local` XML state. No installed content,
production database or Lights checkout is part of release verification.

## Verification

The Release build succeeded, and the full Release suite passed **159 tests,
0 failures, exit 0**, including 28 new correction replacement/approval/editor
cases. Focused verification preceded the full run. The one test-helper correction
normalized Windows path separators; no assertion was weakened.

```powershell
dotnet build AuroraTranslator.Tests/AuroraTranslator.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
./AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe
```

Coverage includes baseline/fingerprint preservation, unchanged companions, all
correction operations, relevant versus unrelated changes, whole-group approval
and reopening, proposed-action eligibility, complete upstream incorporation,
stale/altered snapshots, disabled and invalid typed content, external group
refusals, local/homebrew attribution, existing data-18 refresh, and separate
save/import/retirement boundaries. Existing Staff of Flowers and repaired grant
fixtures remain part of the passing suite.

Build warnings were the existing two CS8632 nullable-context annotations in
`AuroraSqliteImporter` and NU1900 for the unavailable vulnerability feed. No
vulnerability-feed verification is claimed.

## Immutable packages

Both packages were built from clean source commit
`1796636803dd21e5548393070e84e6ecf9ee8695`. Both assemblies report
`0.11.0+1796636803dd21e5548393070e84e6ecf9ee8695`; both NuGet repository
records identify that commit. Content depends on Contracts 0.11.0 and
Microsoft.Data.Sqlite 10.0.12. The later documentation-only commit records this
verification; it is not the packages' source revision.

| Local artifact | SHA-256 |
| --- | --- |
| `Aurora.Content.Contracts.0.11.0.nupkg` | `15ed245ed186e08a41dbce2de0be2ace31d117c8ed02a9e97bf651c25292bf72` |
| `Aurora.Content.0.11.0.nupkg` | `756be3267f2ee68db954a3c51334f5a8584a87bc6b64d7d5f6843789e53331ab` |

Artifacts and `manifest.json` are under `artifacts/packages/0.11.0/`, as ignored
local build outputs. No existing immutable package was overwritten. Package
creation succeeded with the existing missing-readme warnings.

A separate consumer restored the actual NuGet packages into an isolated cache,
using `PackageReference` only, with no project references or friend-assembly
access. Its Release build had zero warnings/errors, and **18 smoke checks passed,
0 failures, exit 0**. The checks cover package/source versions, Contracts access,
embedded schema/import, catalog identity/provenance/aliases, non-mutating reads,
group eligibility, approval previews and saves, normalized database review state,
relevant-change reopening, stale-review refusal, replacement stamp clearing, and
explicit acceptance followed by import-gated retirement. Offline restore used
local artifacts and cached dependencies with NuGet audit disabled.

```powershell
dotnet pack Aurora.Content.Contracts/Aurora.Content.Contracts.csproj -c Release --no-restore -o artifacts/packages/0.11.0 -p:UseSharedCompilation=false -p:RepositoryCommit=1796636803dd21e5548393070e84e6ecf9ee8695 -p:SourceRevisionId=1796636803dd21e5548393070e84e6ecf9ee8695
dotnet pack Aurora.Content/Aurora.Content.csproj -c Release --no-restore -o artifacts/packages/0.11.0 -p:UseSharedCompilation=false -p:RepositoryCommit=1796636803dd21e5548393070e84e6ecf9ee8695 -p:SourceRevisionId=1796636803dd21e5548393070e84e6ecf9ee8695
```

These are provenance commands, not instructions to repack an existing version.
Consumer project/evidence is in `artifacts/package-smoke/0.11.0/` and
`artifacts/release-verification/0.11.0/`.

## Consumer handoff and publication status

Vendor both packages together with their hashes and clean source revision.
Refresh existing prepared databases through import to reach schema/data **1/19**
and preparation contract **2**. The data-18 refresh regression uses existing
unchanged schema tables; it is not a rehearsal against a user's production
content. Never mark an older database current by changing metadata alone.

Reflections integration belongs in the Aurora-Lights context. Use the typed
review groups and preview/save APIs to present deliberate local approval or
upstream acceptance, then separately refresh the database. Display group/blocking
reasons and do not submit one member of a multi-member group. Multi-root hosts
must establish external group membership before using the single-root wrapper;
coordinated cross-file acceptance and a review UI remain follow-ups.

This release is committed and packaged **locally**. No remote push, tag,
NuGet-feed publication, consumer adoption, installed XML edit, or production
database refresh was performed.
