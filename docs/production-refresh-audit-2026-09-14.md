# Production refresh attempt and record audit — September 14, 2026

**The production refresh was blocked during preparation; no candidate was
activated.** The working database and all 1,190 installed XML files remained
unchanged, verified by SHA-256. The complete console suite passed **59/59**.

Lights' configured root is `C:\Users\Ralla\Documents\5e Character Builder\custom`,
with no additional roots. Its working database is `aurora-elements.sqlite` in
that directory. A consistent SQLite backup was saved at
`artifacts/production-refresh-20260914/before.sqlite` before attempting the import.
The original database contains 20,973 elements, 1,189 source-file records, six
mirrored correction files and ten review-pending corrections. Its recorded build
time is September 13, 2026, 11:09:18 UTC.

## Refresh blockers and suggested next work

1. **Document-level append preparation is missing.** The actual standalone
   `sqlite-import` into a backup copy failed on
   `core/dungeon-masters-guide-2024/items-firearms-and-explosives.xml` with the
   explicit unsupported-append diagnostic documented in handoff section 11.
   A read-only inventory, using the existing `LocalCorrectionDocument` evaluator
   for managed corrections, found **1,215 append operations in 55 active files**:
   1,152 supports containers, 63 rules containers and one description container.
   This is a capability gap, not a request to delete those operations.

   Concrete existing-record discrepancies: martial ranged weapon proficiency
   has its five original grants but none of the eight DMG 2024 firearm grants
   supplied by that append. Acid Splash's `spell_access` contains Sorcerer,
   Wizard, Artificer and Spell Saving Throw, but lacks the `UA Artificer` support
   supplied by `unearthed-arcana/20190228.xml`. These are SQLite observations;
   Lights' runtime XML overlay behavior was not exercised in this audit.

   Suggested implementation: prepare appends against explicit target IDs and
   retain the contributing file/package provenance. Reuse and reconcile Lights'
   `DataManager.AppendElements`, `XmlContentFallbackService.ApplyAppendNode`
   and correction evaluator semantics. They differ in some container handling;
   copying one blindly is insufficient. Missing targets, conflicting definitions,
   package availability, replay/idempotence and correction-protected operations
   need explicit handling and fixtures before activation. Do not bypass the
   existing rejection or fall back to the older bundled importer.

2. **Three unmarked local overrides need a recorded decision.** After evaluating
   all seven managed local files in memory, the existing fingerprint contract
   still finds **78 nonmatching same-ID declaration pairs**:

   | Local file under `user/local` | Source file | Affected IDs |
   | --- | --- | ---: |
   | `background-farmer.xml` | `core/players-handbook-2024/background-farmer.xml` | 1 |
   | `players-handbook-2024-items-packs.xml` | `core/players-handbook-2024/items-packs.xml` | 31 |
   | `efa-class.xml` | `the-book-of-xellarant/efa-class.xml` | 46 |

   These are preparation conflicts, not 78 proven gameplay bugs. Review the
   differences, then use the existing protected local XML contract to record
   intended changes, baseline fingerprints and reasons. Do not choose by rank or
   treat matching installed files as acceptance. There are also 22 duplicate-ID
   groups with matching fingerprints, which preparation can consolidate with
   supplier provenance; they do not need arbitrary renaming.

## Existing records and diagnostic accuracy

A separate copy, `relationship-audit.sqlite`, ran the current
`refresh-package-resolution` command. This was **only a derived-relationship
rehearsal**, not an XML refresh or a production candidate. Elements, source files,
raw grant/select declarations, correction mirrors and every package rank,
classification and enabled flag remained unchanged. Consequently it does not
demonstrate the new full import's publisher classification on production data.

- All nine old grant links that pointed to a different Aurora ID became
  unresolved on the rehearsal, as the reviewed identity policy requires.
  Production still has those old links. In particular, Stoneheart still points
  from its nonexistent PHB ID to the Princes of the Apocalypse definition.
  Its authorized Xanathar's correction is installed in XML, but cannot reach
  production SQLite until a successful import. Both real Erupting Earth IDs
  remain present. The other eight proposed spell-reference corrections remain
  uninstalled drafts.
- **462 of 466 reported missing archetype parents have multiple supported
  parents** in the new candidate table. Their singular parent fields are
  intentionally null. The other four belong to disabled Ryoko's content.
  Suggested fix: update the diagnostic view to distinguish supported multiple
  parents, disabled candidates and genuinely absent links. Do not restore a
  guessed parent to quiet the report.
- Of 2,365 unresolved grants on the rehearsal, **2,354 target installed but
  disabled definitions**: 2,021 target Aurora Legacy Essentials and 333 target
  Ryoko's. Preserve those settings; report unavailable dependencies separately
  from broken IDs. If the Essentials disablement was unintended, review enabling
  that package separately. This audit does not establish who disabled it.
- The remaining **11 grants reference nonexistent definitions**: the nine
  previously reviewed spell references, plus Ryoko's
  `ID_RGTTYR_FEAT_FOCUSED_DISCIPLINE_FEATURES` and
  `ID_RGTTYR_FEATURE_REPLACEMENT_BENDER_EXTRA_ATTACK`. The latter two are authored
  in `feats.xml` and `optional-features.xml`; their owning package is disabled.
  Suggested follow-up: review their intended feature targets and create explicit
  corrections, preserving the original grant conditions.
- The source diagnostic reports 93 grants with IDs in `name` attributes. All 93
  already preserve that exact ID in `target_aurora_id`; they are not 93 lost links
  or display-name guesses. It also reports three within-file duplicate IDs; all
  three have matching fingerprints. Triage this output before treating its
  aggregate count as gameplay failures.

The Staff of Flowers DMG and XGTE item definitions both survive. Devout's repaired
grants bind to their exact distinct IDs. Tatsumi's repaired targets remain stored
and are correctly absent from the disabled package's resolution cache. All 40
selection memberships for the four Blazing Dawn infusions are unchanged by the
derived rehearsal. Its selector diagnostics contain 172 character-dependent
cases and one selector without a static target; these remain deferred.

## Verification and evidence

- Both the preserved database and derived rehearsal pass SQLite integrity and
  foreign-key checks. `check-data-integrity` passed on the preserved database,
  including metadata and spellcasting fidelity checks.
- `dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj
  --no-restore` exited 0: **59 passed, zero failed**, with no build warnings or
  errors in the captured log. No test expectations were weakened or rewritten.
  Existing tests correctly cover append rejection; successful full-corpus append
  handling still needs coverage when that capability is implemented.
- All seven installed managed files evaluate as non-retirable. No correction
  was accepted, retired or installed during this audit. No production activation,
  binary publication, commit or push occurred.

Detailed evidence and reproducible read-only audit:
`artifacts/production-refresh-20260914/evidence.json`, `audit.py`,
`effective-inventory.json`, `diagnostic-detail.json`, `append-inventory.json`,
`import.log`, `source-integrity.log` and `unresolved-links.log`.
The existing correction evaluator inventory tool is in
`artifacts/production-refresh-audit-tool`. Full test output is
`artifacts/production-refresh-tests-20260914.log`. These local artifacts are
ignored by Git. The preserved backup is a rollback artifact, not a release.
