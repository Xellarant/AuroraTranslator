# Shared content 0.11.0 release verification

Release preparation: October 6, 2026. Both packages advance from 0.10.1 to
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

Package creation and a package-only consumer smoke test follow a clean source
commit. Their immutable artifacts, hashes and source provenance will be recorded
below after verification. No existing package version will be overwritten.
