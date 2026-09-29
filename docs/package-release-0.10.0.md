# Shared content 0.10.0 release verification

Verified September 29, 2026. The catalog summary/detail API is included in
`Aurora.Content` **0.10.0**, paired with `Aurora.Content.Contracts` **0.10.0**.
Database schema/data remain **1/17**; this release requires no new migration.

Both packages were built in Release from clean source commit
`61a0654086e3884793ca156377bf8068a4efc27a`. The later documentation commit records
verification only and is not the packages' source revision. Both assemblies report
`0.10.0+61a0654086e3884793ca156377bf8068a4efc27a`, and both NuGet repository metadata
entries identify that same commit. The Content package depends on Contracts
0.10.0 and Microsoft.Data.Sqlite 10.0.12.

| Local artifact | SHA-256 |
| --- | --- |
| `Aurora.Content.Contracts.0.10.0.nupkg` | `a76c8c2b2a1353f103f865c13a8188be7496242ed065f2a0b2feceb02a1cd2c6` |
| `Aurora.Content.0.10.0.nupkg` | `d581e623549435e6cbde56e26eaafa36d25d6f588b38853cc81ba7936ecd1111` |

Artifacts and the machine-readable provenance manifest are in
`artifacts/packages/0.10.0/`. These are ignored build outputs, not files committed
to the source repository. Existing immutable package versions were not overwritten.

## Verification

- `dotnet build AuroraTranslator.sln -c Release -m:1 -p:UseSharedCompilation=false`
  succeeded with zero errors.
- `AuroraTranslator.Tests/bin/Release/net10.0/AuroraTranslator.Tests.exe` passed
  **124 tests, 0 failures, exit code 0**.
- A disposable external consumer referenced the actual `Aurora.Content` NuGet
  package, with no project references or friend-assembly access. Restore used an
  isolated package cache, the new local packages and cached third-party packages.
  Its Release build had zero warnings/errors; **9 smoke checks passed, exit code 0**.
- The smoke checks verified both assembly versions/source commits, transitive
  Contracts API access, the embedded import schema, summaries, distinct identities,
  provisional conflicts, UA classification, spell facets, descriptions, conditional
  grants, append provenance, missing references, aliases and unchanged database bytes
  after reads. All imported content/databases were disposable fixtures.
- Package IDs, versions, target framework, dependency versions, repository commit
  metadata and SHA-256 hashes were inspected after packaging.

The Release build/pack reported the two existing CS8632 nullable-context warnings
at `AuroraSqliteImporter.cs:3307-3308`. NU1900 indicated the NuGet vulnerability
feed was unavailable; the offline consumer restore disabled that audit for that
invocation, so vulnerability-feed verification is not claimed. Pack also reported
the existing lack of package readmes. None prevented build, tests, or packaging.

Local logs and the disposable consumer are retained under
`artifacts/release-verification/0.10.0/` and `artifacts/package-smoke/0.10.0/`.
At verification, no remote push, tag, NuGet-feed publication or Lights/Web adoption
was performed. Installed content and production databases were unchanged.
