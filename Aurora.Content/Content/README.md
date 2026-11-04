# Shared content preparation

`Aurora.Content.Contracts/LocalCorrectionDocument.cs` began as a port of the
Aurora-Lights evaluator at checkpoint `624b6b7cec82e8ad6d0efffb36ba414908469a15`.
It is now the shared packaged implementation, with correction replacement,
durable local approval, and grouped acceptance added since that checkpoint.
It is no longer a byte-for-byte copy. Preserve the XML contract and existing
structural fingerprint semantics; ship coordinated changes in a new immutable
package version rather than maintaining a second evaluator in a consumer.

`ContentPreparation` captures original XML, evaluates corrections, resolves
explicit declaration decisions, and materializes appends. The SQLite writer
persists finalized declarations. `PreparedContentWriter` validates their coverage,
records superseded identities and provenance, and updates the completed row count.
`LocalCorrectionSync` coordinates candidate validation, race detection, activation,
and recoverable retirement. `ContentImport.ImportAsync` and the CLI's
`sqlite-import` command use this workflow. Legacy direct catalog import remains a
separate compatibility path.

Source-integrity inspection compares current input bytes with captured input
fingerprints and normalized database rows with the stored prepared snapshot.
Original XML and corrected/composed output are intentionally different artifacts.
The check does not replay an import, approve corrections, retire files, or clear
recorded import warnings.

Installed-file matches never cause automatic acceptance. Local approval keeps
corrections active and protected; relevant changes reopen review. Explicit
same-file review APIs are documented in the [editor contract](../../docs/correction-editor-contract.md).
No verified-download evidence producer or coordinated cross-file transaction is
implemented here. Reflections presentation and adoption are maintained in the
Aurora-Lights context.

See the [handoff](../../docs/aurora-translator-data-handoff.md) for supported scope,
verification and release status. Original regression fixtures retain their
whitespace and provenance; reconstructed baselines are not complete archives of
upstream publications.
