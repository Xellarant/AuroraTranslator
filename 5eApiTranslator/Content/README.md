# Content preparation port

`LocalCorrectionDocument.cs` is an unchanged, byte-for-byte port of
`Builder.Data/Files/LocalCorrectionDocument.cs` from Aurora-Lights checkpoint
`624b6b7cec82e8ad6d0efffb36ba414908469a15`. Its SHA-256 is
`4D754E96F67BDB438733A484306C6A57450C11BF33F5D21EB2F7A1E15FEFB6BF`.
Keep the namespace, serializer and fingerprint algorithm compatible. Changes to
the contract/evaluator must be coordinated with Lights until shared packaging is
introduced; this port deliberately does not introduce another evaluator.

`ContentPreparation` captures XML, evaluates that contract and finalizes
declarations. `AuroraSqliteImporter.ImportFinalized` validates catalog identity
before writing. `PreparedContentWriter` validates candidate coverage and persists
declaration provenance. `LocalCorrectionSync` adapts the Lights lifecycle wrapper
to coordinate candidate backup/import, mirrors, race detection, activation and
recoverable retirement. The CLI is the supported production entry point in this
slice; legacy raw-catalog callers have not been migrated.

Installed-file matches never cause acceptance. No downloader evidence producer
or automatic acceptance path is implemented. The explicit review API is the
ported `AcceptUpstream`, including reviewed input hashes and grouped validation.

See [the handoff](../../docs/aurora-translator-data-handoff.md) for boundaries,
storage semantics and focused verification. The copied regression fixtures retain
their original whitespace and provenance. Test baselines reconstruct selected
malformed declarations; they are not the full archived upstream originals.
