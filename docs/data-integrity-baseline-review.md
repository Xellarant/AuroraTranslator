# Data Integrity Baseline Review

## September 12, 2026 Snapshot

The version 11 baseline uses the installed `core` and `supplements` XML corpus. Before capture, the importer verifies SQLite structure, foreign keys, metadata, source file hashes, element identities, and complete spellcasting declarations against the staged XML.

Compared with the previous WPF baseline:

| Surface | Previous | Current |
| --- | ---: | ---: |
| Data version | 9 | 11 |
| Source files | 631 | 649 |
| Elements | 10,827 | 11,648 |
| Spellcasting profiles | 72 | 72 |
| Extended profiles | 50 | 50 |
| Spell access rows | 3,197 | 3,241 |
| Companions | 389 | 398 |

The corpus comparison found 20 added files and two removed files. Additions comprise 18 Dungeon Master's Guide 2024 files and the Monster Manual 2025 source/creatures files. Removed files are `core/monster-manual/languages.xml` and `core/players-handbook-2024/companions.xml`. Existing curated spellcasting profile samples are unchanged; the new baseline also records all ordered entries, potential recipients, explicit references, and source hashes.

Spellcasting now preserves 22 initial-list entries and 594 extension entries. There are 640 potential extension-recipient relationships: 594 from `all="true"` and 46 from profile-name matching. These counts describe installed content, not active character spellcasting.

## Source Issues Retained

The pre-existing general diagnostics contain zero actionable unresolved importer links, 490 deferred option-pool parent links, and 10 source-integrity findings. Their prior counts were zero, 478, and nine respectively. The additional source-integrity finding is a duplicate ID in the newly imported DMG 2024 weapons file: both Adamantine Ammunition and Walloping Ammunition use `ID_WOTC_DMG24_MAGIC_ITEM_AMMUNITION_ADAMANTINE` (source lines 25 and 1332).

The new spellcasting reference view exposes 589 resolved references and one unresolved reference. `supplements/eberron-rising-from-the-last-war/mark-of-storm.xml`, line 99, names `ID_PHB_SPELL_CONJURE_MINOR_ELEMENTAL`. That exact ID is absent from the imported spell set. It remains unchanged and unresolved so consumers can inspect the source error. This spellcasting diagnostic is separate from the existing general unresolved-link classification.

Accepting these source diagnostics as a baseline records their presence; it does not correct or endorse the underlying XML. Future changes to them remain visible in regression checks.

## Compatibility And Follow-Up

- Aurora-Lights currently declares data version 10. Its version gate and importer need a separately reviewed update before adopting version 11 snapshots.
- Package refresh referenced a nonexistent subrace `parent_element_id` column. It now checks `race_element_id`; the package-switch fixture exercises the public refresh path and checks spellcasting recipients before and after an override is disabled.
- NuGet restore reported `NU1903` for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q). Dependency remediation remains a separate follow-up; this is not the earlier offline vulnerability-feed warning.

These checks establish general structural integrity and focused spellcasting fidelity. They do not establish complete WPF semantic parity for every content family.

## Verification Results

- Build succeeded, and all 17 focused tests passed against the new snapshot.
- Source comparison passed for all 649 imported files and their element identities, plus all 72 spellcasting declarations. Updated WPF and general diagnostics baseline checks passed.
- The broader optional character-evaluator sweep passed 20 of 35 existing baselines. Their expected results were retained for follow-up review.

Of the 15 character baseline mismatches, 12 also fail when the same evaluator uses the older local regression database: Druid Primal Order direct state, the three Life Domain fixtures, both Monk fixtures, Rogue Expertise partial/complete, Sorcerer Metamagic complete/overpick, Warlock Invocations complete, and Warlock spellcasting overpick. Observed differences include saved picks rejected by ownership or ambiguity guards, direct-state over-selection, and stale choice-provenance expectations.

Three additional mismatches are specific to the refreshed corpus: Badger's old PHB 2024 companion source was removed, while Druid Primal Order complete and Wizard Abjuration have changed spell-option sets. These evaluator fixtures need a separate review against their intended scenarios before updating expected results. The existing local regression database was retained; the verified version 11 audit snapshot is in `artifacts/spellcasting-audit/first-party.sqlite` and can be regenerated with the documented first-party capture command.
