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
pre-commit review found no additional blocker. Package-consumer verification and
immutable package provenance will be recorded after packing the clean source.

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

## Release boundaries

Packages must be built from the clean committed source, using a new immutable
version. No remote push, tag, NuGet-feed publication, consumer repository change,
installed XML edit, or production database refresh is part of this closeout.
Consumer adoption and its own regression checks belong in the Aurora-Lights
context. The supported core is ready for maintenance after this release's checks;
multi-root orchestration, cross-file review, publisher-download evidence, broader
evaluator coverage and a Translator desktop UI remain separate future work.
