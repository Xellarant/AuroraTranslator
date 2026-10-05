# Shared content 0.10.1 release verification

Release work dated October 5, 2026. `Aurora.Content` and
`Aurora.Content.Contracts` advance together from 0.10.0 to **0.10.1**.
This fixes two append differences reported against Lights' pinned 0.9.0 and
confirmed still present in Translator's 0.10.0 implementation.

## Behavior and database compatibility

- Import preparation and runtime replay use the existing Legacy relative-path
  load-order helper: supplements precede homebrew, a directory's own files precede
  descendants, and append nodes retain their file order. Catalog append provenance
  uses the same ordering.
- Appended descriptions are ignored, including when the target has no description.
  Their original XML remains inspectable in operation provenance.
- Database schema stays **1**; data advances **17 -> 18**, and the preparation
  contract advances **1 -> 2**. Existing prepared databases require an XML refresh.
  Normalized descriptions/grants and stored effective XML must be recomposed;
  changing metadata alone cannot migrate them.
- Package/schema administration no longer promotes the semantic data version.
  Successful import stamps the new version after writing finalized content.
- If unreadable suppliers would force retention of old composed append effects,
  upgrade stops with an ID/supplier/repair diagnostic and preserves the working
  database. Repair those suppliers and refresh. Current-policy retention,
  unextended older definitions, and deliberate removal of a base remain supported.

The order fixture demonstrates grant ordering, not changed character statistics.
The shared comparer retains deterministic ordinal ties within one directory and
between sibling directories; Legacy does not explicitly sort filesystem
enumeration. Configured priority between multiple content roots is not covered.
Other append policies, duplicate resolution, corrections, and app filtering are
unchanged.

## Verification

The Release test-project build succeeded. All **131 test cases are verified**:
the full run passed 130 and found one historical assertion expecting package
administration to advance the data version. The assertion now requires the
version-refresh diagnostic and preserved version 10; its missing-spellcasting
checks and refresh-versus-fresh comparison remain. That focused rerun passed,
exit 0. No production code changed after the full run.

```powershell
dotnet build AuroraTranslator.Tests/AuroraTranslator.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
./AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe
# After correcting the historical metadata assertion and rebuilding:
./AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe 'reimports legacy spellcasting'
```

Clean-source packaging and package-consumer verification follow the source commit.

A disposable oracle compiled copies of Lights'
`tools/ContentDatabaseRehearsal/LegacyAppendAudit.cs` against the updated shared
library and the existing Legacy binaries. Only its mock preparation contract was
updated to 2; a second case removed the base description. Both cases passed:

| Case | Legacy | Updated shared library |
| --- | --- | --- |
| Base description plus appended prose | `<p>Base</p>` | `<p>Base</p>` |
| Absent base description plus appended prose | empty | empty |
| Grant order in both cases | supplement, homebrew | supplement, homebrew |

The original prebuilt 0.9.0 reproduction failed with homebrew before supplement
and combined `Base + Extra` prose. This independent Legacy comparison complements
the committed regressions; XML/database parity is not used as the sole oracle.

Regression fixtures cover normalized rows, raw append provenance, repeated rules,
within-file append order, stored/runtime replay, host targets, genuine old
materialized output, refresh versus fresh import, administrative metadata
integrity, old retained append history, repair, and database preservation on failure.

Build warnings are the existing two CS8632 nullable-context annotations in
`AuroraSqliteImporter` and NU1900 because the vulnerability feed was unavailable.
Vulnerability-feed verification is not claimed.

## Consumer handoff

Lights adoption belongs in its own repository context. Vendor both immutable
0.10.1 packages with their clean source revision and hashes, then run its consumer
tests and actual Legacy rehearsal. The rehearsal's mock preparation metadata must
use contract 2; retain its original Legacy expectations. Regenerate disposable
existing prepared databases via import before checking full-loader parity and
restart behavior. A raw SQL version update is not an upgrade.

No installed content, production database, or Lights source files were changed.
Local evidence is under `artifacts/append-policy-0.10.1/` and
`artifacts/release-verification/0.10.1/`.
