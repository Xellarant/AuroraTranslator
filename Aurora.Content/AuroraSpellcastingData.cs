using AuroraTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AuroraTranslator;

internal static partial class AuroraSqliteImporter
{
    internal const string SpellcastingViewsSql = """
DROP VIEW IF EXISTS v_spellcasting_definitions;
CREATE VIEW v_spellcasting_definitions AS
SELECT sp.*, e.aurora_id AS owner_aurora_id, e.name AS owner_name,
       et.type_name AS owner_type_name, rec.package_key AS owner_package_key,
       sf.relative_path AS owner_source_path,
       CASE WHEN sp.raw_xml IS NULL THEN 1 ELSE 0 END AS requires_xml_reimport,
       CASE WHEN sp.raw_xml IS NULL THEN NULL WHEN sp.is_extended = 0 THEN
           COALESCE((SELECT spe.is_known FROM spellcasting_profile_entries spe
            WHERE spe.spellcasting_profile_id = sp.spellcasting_profile_id AND spe.entry_kind = 'list'
            ORDER BY spe.ordinal DESC LIMIT 1), 0)
       ELSE 0 END AS prepare_from_spell_list
FROM spellcasting_profiles sp
JOIN elements e ON e.element_id = sp.owner_element_id
JOIN resolved_elements_cache rec ON rec.winning_element_id = e.element_id
JOIN source_files sf ON sf.source_file_id = e.source_file_id
JOIN element_types et ON et.element_type_id = e.element_type_id;

DROP VIEW IF EXISTS v_spellcasting_profile_entries;
CREATE VIEW v_spellcasting_profile_entries AS
SELECT sp.spellcasting_profile_id, sp.owner_element_id, sp.owner_aurora_id, sp.owner_name,
       sp.owner_package_key, sp.owner_source_path, sp.owner_type_name, sp.owner_kind,
       sp.profile_name, sp.ability_name, sp.is_extended, sp.prepare_spells, sp.allow_replace,
       spe.entry_kind, spe.ordinal AS entry_ordinal, spe.entry_text,
       spe.spellcasting_profile_entry_id, spe.is_known, spe.raw_xml AS entry_raw_xml,
       sp.assign_to_all, sp.requires_xml_reimport
FROM spellcasting_profile_entries spe
JOIN v_spellcasting_definitions sp ON sp.spellcasting_profile_id = spe.spellcasting_profile_id;

-- These are potential recipients. The consumer must establish that both owners are active.
DROP VIEW IF EXISTS v_spellcasting_extension_targets;
CREATE VIEW v_spellcasting_extension_targets AS
SELECT ext.spellcasting_profile_id AS extension_profile_id,
       ext.owner_element_id AS extension_owner_element_id,
       ext.owner_aurora_id AS extension_owner_aurora_id,
       ext.owner_package_key AS extension_owner_package_key,
       ext.owner_source_path AS extension_owner_source_path,
       ext.profile_name AS target_profile_name, ext.assign_to_all,
       base.spellcasting_profile_id AS recipient_profile_id,
       base.owner_element_id AS recipient_owner_element_id,
       base.owner_aurora_id AS recipient_owner_aurora_id,
       base.owner_package_key AS recipient_owner_package_key,
       base.owner_source_path AS recipient_owner_source_path,
       CASE WHEN ext.requires_xml_reimport = 1 THEN 'requires-xml-reimport'
            WHEN base.spellcasting_profile_id IS NULL THEN 'unresolved'
            WHEN ext.assign_to_all = 1 THEN 'all-profiles'
            ELSE 'profile-name' END AS binding_kind,
       1 AS requires_character_context
FROM v_spellcasting_definitions ext
LEFT JOIN v_spellcasting_definitions base
  ON base.is_extended = 0 AND ext.requires_xml_reimport = 0 AND base.requires_xml_reimport = 0
 AND (ext.assign_to_all = 1 OR ext.profile_name = base.profile_name COLLATE NOCASE)
WHERE ext.is_extended = 1;

DROP VIEW IF EXISTS v_spellcasting_list_contributions;
CREATE VIEW v_spellcasting_list_contributions AS
SELECT sp.spellcasting_profile_id AS recipient_profile_id,
       sp.owner_element_id AS recipient_owner_element_id,
       sp.owner_aurora_id AS recipient_owner_aurora_id,
       spe.spellcasting_profile_entry_id, sp.spellcasting_profile_id AS contribution_profile_id,
       sp.owner_element_id AS contribution_owner_element_id,
       sp.owner_aurora_id AS contribution_owner_aurora_id,
       sp.owner_package_key AS contribution_package_key, sp.owner_source_path AS contribution_source_path,
       spe.entry_kind, spe.ordinal AS entry_ordinal, spe.entry_text, spe.is_known,
       'local' AS binding_kind, 1 AS requires_character_context
FROM v_spellcasting_definitions sp
JOIN spellcasting_profile_entries spe ON spe.spellcasting_profile_id = sp.spellcasting_profile_id
WHERE sp.is_extended = 0 AND sp.requires_xml_reimport = 0
  AND (spe.entry_kind = 'extend' OR spe.ordinal =
      (SELECT MAX(initial.ordinal) FROM spellcasting_profile_entries initial
       WHERE initial.spellcasting_profile_id = sp.spellcasting_profile_id AND initial.entry_kind = 'list'))
UNION ALL
SELECT target.recipient_profile_id, target.recipient_owner_element_id, target.recipient_owner_aurora_id,
       spe.spellcasting_profile_entry_id, target.extension_profile_id,
       target.extension_owner_element_id, target.extension_owner_aurora_id,
       target.extension_owner_package_key, target.extension_owner_source_path,
       spe.entry_kind, spe.ordinal, spe.entry_text, spe.is_known, target.binding_kind, 1
FROM v_spellcasting_extension_targets target
JOIN spellcasting_profile_entries spe ON spe.spellcasting_profile_id = target.extension_profile_id
WHERE target.recipient_profile_id IS NOT NULL AND spe.entry_kind = 'extend';

-- WPF treats an extension beginning with ID_ as explicit IDs; other text remains an expression.
DROP VIEW IF EXISTS v_spellcasting_entry_spell_references;
CREATE VIEW v_spellcasting_entry_spell_references AS
WITH RECURSIVE ids(entry_id, ordinal, reference_text, remainder) AS
(
    SELECT spellcasting_profile_entry_id, 0, '', trim(entry_text) || ','
    FROM v_spellcasting_profile_entries
    WHERE entry_kind = 'extend' AND substr(trim(entry_text), 1, 3) = 'ID_'
    UNION ALL
    SELECT entry_id, ordinal + 1, trim(substr(remainder, 1, instr(remainder, ',') - 1)),
           substr(remainder, instr(remainder, ',') + 1)
    FROM ids WHERE remainder <> ''
)
SELECT ids.entry_id AS spellcasting_profile_entry_id, ids.ordinal AS reference_ordinal,
       ids.reference_text AS spell_aurora_id, spell.element_id AS spell_element_id,
       e.name AS spell_name, rec.package_key AS spell_package_key,
       CASE WHEN spell.element_id IS NULL THEN 'unresolved' ELSE 'resolved' END AS resolution_status
FROM ids
LEFT JOIN resolved_elements_cache rec ON rec.aurora_id = ids.reference_text
LEFT JOIN spells spell ON spell.element_id = rec.winning_element_id
LEFT JOIN elements e ON e.element_id = spell.element_id
WHERE ids.ordinal > 0 AND ids.reference_text <> '';
""";

    private static void EnsureSpellcastingFidelityColumns(SqliteConnection connection)
    {
        EnsureColumnExistsIfTableExists(connection, "spellcasting_profiles", "assign_to_all", "INTEGER CHECK (assign_to_all IN (0, 1))");
        EnsureColumnExistsIfTableExists(connection, "spellcasting_profiles", "raw_xml", "TEXT");
        EnsureColumnExistsIfTableExists(connection, "spellcasting_profile_entries", "is_known", "INTEGER CHECK (is_known IN (0, 1))");
        EnsureColumnExistsIfTableExists(connection, "spellcasting_profile_entries", "raw_xml", "TEXT");
    }

    internal sealed record SpellcastingEntryRow(string Kind, int Ordinal, string Text, bool? Known, string RawXml);

    internal static List<SpellcastingEntryRow> ExpectedSpellcastingEntries(string rawXml, string listText, string extendText)
    {
        if (rawXml != null)
            return AuroraSpellcastingXml.Parse(XElement.Parse(rawXml)).entries
                .Select(entry => new SpellcastingEntryRow(entry.kind, entry.ordinal, entry.text, entry.known, entry.rawXml))
                .OrderBy(entry => entry.Kind, StringComparer.Ordinal).ThenBy(entry => entry.Ordinal).ToList();

        // Legacy flattened text cannot recover XML child boundaries or known/all flags.
        return new[] { ("list", listText), ("extend", extendText) }
            .SelectMany(pair => ParseSpellcastingProfileEntryText(pair.Item2)
                .Select((text, index) => new SpellcastingEntryRow(pair.Item1, index + 1, text, null, null)))
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal).ThenBy(entry => entry.Ordinal).ToList();
    }

    internal static List<SpellcastingEntryRow> ReadSpellcastingEntries(SqliteConnection connection, long profileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT entry_kind, ordinal, entry_text, is_known, raw_xml
FROM spellcasting_profile_entries WHERE spellcasting_profile_id = $id
ORDER BY entry_kind, ordinal;
""";
        command.Parameters.AddWithValue("$id", profileId);
        using var reader = command.ExecuteReader();
        var rows = new List<SpellcastingEntryRow>();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return rows;
    }

    private static void RepairSpellcastingEntries(SqliteConnection connection)
    {
        var profiles = new List<(long Id, string RawXml, string List, string Extend)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT spellcasting_profile_id, raw_xml, list_text, extend_text FROM spellcasting_profiles;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                profiles.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        foreach (var profile in profiles)
        {
            var expected = ExpectedSpellcastingEntries(profile.RawXml, profile.List, profile.Extend);
            if (expected.SequenceEqual(ReadSpellcastingEntries(connection, profile.Id)))
                continue;
            using var transaction = connection.BeginTransaction();
            ExecuteInsert(connection, transaction, "DELETE FROM spellcasting_profile_entries WHERE spellcasting_profile_id = $id;", ("$id", profile.Id));
            InsertSpellcastingEntryRows(connection, transaction, profile.Id, expected);
            transaction.Commit();
        }
    }

    private static void InsertFaithfulSpellcastingEntries(SqliteConnection connection, SqliteTransaction transaction, long id, Spellcasting profile)
        => InsertSpellcastingEntryRows(connection, transaction, id,
            profile.entries != null
                ? profile.entries.Select(entry => new SpellcastingEntryRow(entry.kind, entry.ordinal, entry.text, entry.known, entry.rawXml))
                : ExpectedSpellcastingEntries(profile.rawXml, profile.list?.raw, profile.extendList?.raw));

    private static void InsertSpellcastingEntryRows(SqliteConnection connection, SqliteTransaction transaction, long id, IEnumerable<SpellcastingEntryRow> entries)
    {
        foreach (var entry in entries)
            ExecuteInsert(connection, transaction, """
INSERT INTO spellcasting_profile_entries (spellcasting_profile_id, entry_kind, ordinal, entry_text, is_known, raw_xml)
VALUES ($id, $kind, $ordinal, $text, $known, $xml);
""",
                ("$id", id), ("$kind", entry.Kind), ("$ordinal", entry.Ordinal), ("$text", entry.Text),
                ("$known", (object)entry.Known ?? DBNull.Value), ("$xml", (object)entry.RawXml ?? DBNull.Value));
    }
}
