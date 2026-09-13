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
- The initial restore reported `NU1903` for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q), not the earlier offline vulnerability-feed warning. The follow-up upgrade to `Microsoft.Data.Sqlite` 10.0.12 resolves SQLitePCLRaw 2.1.12 and loads native SQLite 3.53.3. The live NuGet audit reports no vulnerable packages. A runtime regression check requires SQLite 3.50.2 or newer for CVE-2025-6965.

These checks establish general structural integrity and focused spellcasting fidelity. They do not establish complete WPF semantic parity for every content family.

## Initial Verification Results

- Build succeeded, and all 17 focused tests passed against the new snapshot.
- Source comparison passed for all 649 imported files and their element identities, plus all 72 spellcasting declarations. Updated WPF and general diagnostics baseline checks passed.
- The broader optional character-evaluator sweep passed 20 of 35 existing baselines. Their expected results were retained for follow-up review.

Of the 15 character baseline mismatches, 12 also failed when the same evaluator used the older local regression database: Druid Primal Order direct state, the three Life Domain fixtures, both Monk fixtures, Rogue Expertise partial/complete, Sorcerer Metamagic complete/overpick, Warlock Invocations complete, and Warlock spellcasting overpick. That comparison established that those failures were not specific to the corpus refresh, not that they were harmless.

Three additional mismatches were specific to the refreshed corpus: Badger's old PHB 2024 companion source was removed, while Druid Primal Order complete and Wizard Abjuration had changed spell-option sets. The existing local regression database was retained; the verified version 11 audit snapshot is in `artifacts/spellcasting-audit/first-party.sqlite` and can be regenerated with the documented first-party capture command.

## Evaluator Cleanup

The follow-up review found two evaluator defects, both reproduced with failing tests before fixing them:

- Automatic feature selection treated Warden as a deterministic default after an already-owned Magician became unavailable. It now checks the existing choice satisfaction count before adding a default. Direct Magician/Warden, explicit selections, unselected Primal Order, and legitimate singleton defaults have regression coverage.
- Implicit defaults stored row-keyed choice values, but computed provenance only recovered their labels through explicit saved choices. Replacement tokens could hide the original selects, leaving valid features without choice attribution. Evaluation now retains implicit select metadata through completion. Life Domain, Life Domain oversubscribe, and Monk Focus pass their original baselines without recapture.

The other 12 baselines were reviewed before recapture:

| Fixtures | Reviewed change |
| --- | --- |
| Badger | Select the exact MM 2025 companion ID/package. Burrow 5, walk 20, and darkvision 30 remain verified. |
| Druid direct/complete, Wizard Abjuration | Current POTA XML changes Catapult's range/text, Ice Knife, Thunderclap, Dust Devil, and Magic Stone wording. Matching reprints collapse only in the evaluator's option projection; distinct variants remain. POTA Warding Wind also gains Wizard list access. The imported spell rows are not removed. |
| Life Domain complete | Choose Guiding Bolt for Magic Initiate instead of Bless, which Life Domain already grants as prepared. |
| Monk complete | Choose Flute instead of Calligrapher's supplies, which Acolyte already grants. The fixture completes its skill/tool/language slice, not the remaining race/background choices. |
| Rogue Expertise partial/complete | Choose Sleight of Hand instead of Perception, which Elf already grants. The partial fixture still leaves two Expertise picks open; the completed fixture fills both. Elven subrace remains intentionally pending. |
| Sorcerer Metamagic complete/overpick | Address the level-one spell row with its stable `choiceRowKey`, then add Mage Armor and Burning Hands to the separate level-two row. The overpick still rejects Distant Spell with `select-full`. |
| Warlock Invocations complete, spellcasting overpick | Address level-one and level-two spell rows separately. Add the legitimate level-two spell instead of reusing the level-one selections. The overpick still rejects Chill Touch and the extra level-one Charm Person; it legitimately learns Hellish Rebuke at level two. Elven subrace remains pending. |

`EvaluatorFixtureTests` checks these scenario invariants independently of saved baseline values. No ownership or ambiguity guard was relaxed, no XML was edited, and the database model remains a secondary representation rather than an authoritative rules engine.

### Follow-Up Verification

- All 35 character-state baseline pairs pass against `artifacts/spellcasting-audit/first-party.sqlite` after the reviewed corrections (previously 20/35).
- All 25 focused console-harness tests pass against the same snapshot, including eight added runtime/evaluator/fixture checks.
- The read-only database integrity check passes on the upgraded runtime. Native SQLite reports 3.53.3, and the online transitive NuGet vulnerability audit reports no vulnerable packages.
- `git diff --check` passes. JSON uses LF and the other touched files retain their existing CRLF convention. Aurora-Lights and the published handoff snapshot were not modified.
