# SQLite Spellcasting Contract

XML is authoritative. Data version 11 preserves spellcasting declarations so a consumer can search their relationships and inspect the source. These views describe installed content and potential relationships; they do not declare which features, extensions, or spells a character owns.

## Definitions And Ownership

- `v_spellcasting_definitions` exposes each winning element's profile, source/package, ability, preparation/replacement flags, `assign_to_all`, raw XML, and `requires_xml_reimport`.
- `v_spellcasting_profile_entries` contains one row per XML `<list>` or `<extend>` child, preserving order, the complete expression, `known`, and raw XML. Commas inside expressions are not rewritten as OR alternatives.
- `v_spellcasting_extension_targets` associates an extension with every potential non-extension recipient having the same profile name, or every base profile when `all="true"`. Both the contributing owner and recipient owner remain explicit. Missing recipients remain visible as `unresolved` rows.
- `v_spellcasting_list_contributions` combines a recipient's own list/extensions and potential external extensions. `requires_character_context=1` means the consumer must establish active owners and evaluate their conditions before applying a contribution.
- `v_spellcasting_entry_spell_references` resolves explicit spell IDs from extension entries through package winner resolution. Unresolved IDs remain visible. Different Aurora IDs, including different editions of a spell with the same name, remain separate.

A profile name is a matching label, not a unique owner ID. For example, two installed editions of Warlock may both be potential recipients. The consuming character model decides which owner is active. Database integer IDs identify rows in one snapshot; retain Aurora ID and source/package provenance when correlating across refreshes.

The existing `v_spellcasting_profiles` view summarizes granted spells. Use `v_spellcasting_definitions` for XML profile declarations. An expanded spell list is not automatically a spell grant or a prepared spell; `known` is preserved separately from grant preparation metadata.

WPF reference behavior comes from `Builder.Data/ElementParser.cs` and `Builder.Data/Elements/SpellcastingInformation.cs`, plus `Aurora.Logic/CharacterManager.cs` and `Aurora.Lights/ViewModels/SelectionRuleExpanderViewModel.cs` in Aurora-Lights. The first spellcasting block is active; additional blocks are preserved in `element_blocks`. Within that block, WPF uses the last `<list>` child as its initial list and retains every `<extend>` child. A known initial list on a base profile sets `prepare_from_spell_list`.

## Query Examples

Inspect all declarations that might contribute to one installed profile:

```sql
SELECT contribution_owner_aurora_id, contribution_package_key,
       contribution_source_path, entry_kind, entry_text, is_known, binding_kind
FROM v_spellcasting_list_contributions
WHERE recipient_profile_id = $profile_id
ORDER BY contribution_profile_id, entry_ordinal;
```

Resolve explicit spell references without treating expressions as resolved spell lists:

```sql
SELECT c.recipient_owner_aurora_id, c.contribution_owner_aurora_id,
       r.spell_aurora_id, r.spell_name, r.resolution_status, c.is_known
FROM v_spellcasting_list_contributions AS c
JOIN v_spellcasting_entry_spell_references AS r
  USING (spellcasting_profile_entry_id)
WHERE c.recipient_profile_id = $profile_id;
```

Non-ID entries such as `Wizard,Evocation` and dynamic macros remain intact in `entry_text`. These queries intentionally do not claim to enumerate their matching spells or enforce character eligibility.

## Refresh And Verification

Version 10 flattened text cannot reconstruct missing sibling extensions, child flags, or `all`. Migration adds the version 11 columns but marks such profiles `requires_xml_reimport=1`, with unknown flags left null. Their source file hashes are invalidated so the next XML import reloads them even if the files are unchanged. Potential list contributions are withheld until that reimport. With raw XML available, maintenance can repair missing, modified, or extra normalized entry rows, including partial damage within one entry kind.

```powershell
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-data-integrity [sqlitePath] [optionalAuroraRoot]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-wpf-parity-regression [sqlitePath] [baselinePath]
```

Integrity checking is read-only and fails on SQLite corruption, foreign-key violations, metadata count/version mismatches, incomplete spellcasting imports, and disagreement between stored spellcasting XML and its projections. With an XML root supplied it also checks every imported source file's hash, element identities (ID, name, type, and source), and spellcasting declarations. This provides general structural and focused spellcasting fidelity evidence, not complete semantic parity for every Aurora construct.

The WPF baseline captures source hashes and spellcasting entry, recipient, and reference keys in addition to existing structural checks. Review corpus changes before accepting a new baseline. A source-authored dangling spell ID can be faithfully imported and pass integrity while appearing as unresolved in reference diagnostics.

Element identity checks compare type and source-book names without case, matching existing importer normalization. Element IDs and names remain exact comparisons.

The focused tests use `Data/spellcasting-fidelity-example.xml`, a small synthetic fixture reflecting WPF declaration shapes. Set `AURORA_TEST_DATABASE` to run the other corpus-dependent tests against a particular snapshot.

Consumers currently expecting data version 10 need a separate compatibility update before adopting a version 11 snapshot. This translator change does not update Aurora-Lights' importer or version gate.
