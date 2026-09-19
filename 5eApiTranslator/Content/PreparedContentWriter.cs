#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AuroraTranslator.Content;

/// <summary>Validates the candidate against finalized declarations and persists their provenance.</summary>
internal static class PreparedContentWriter
{
    internal static void ValidateDatabase(SqliteConnection connection, ContentPreparation prepared)
    {
        var expected = prepared.Declarations.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT aurora_id FROM elements";
        var actual = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if (!actual.Add(reader.GetString(0))) throw new InvalidDataException("Candidate contains duplicate Aurora IDs.");
        if (!expected.SetEquals(actual))
            throw new InvalidDataException($"Candidate does not contain the finalized declarations. Missing: {string.Join(", ", expected.Except(actual))}; unexpected: {string.Join(", ", actual.Except(expected))}.");
        command.CommandText = """
            SELECT COUNT(*) FROM elements e
            LEFT JOIN resolved_elements_cache r ON r.aurora_id=e.aurora_id
            WHERE r.aurora_id IS NULL OR r.winning_element_id<>e.element_id
            """;
        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            throw new InvalidDataException("Finalized elements do not match the unrestricted catalog resolution cache.");
        ValidateAppendEffects(connection, prepared);
    }

    private static void ValidateAppendEffects(SqliteConnection connection, ContentPreparation prepared)
    {
        var extendedIds = prepared.Appends.Where(a => a.Status == "applied").Select(a => a.TargetId).ToHashSet(StringComparer.Ordinal);
        foreach (var element in prepared.Finalized.Where(e => extendedIds.Contains(e.Id)))
        {
            var xml = XElement.Parse(element.EffectiveXml);
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$id", element.Id);
            bool spell = string.Equals((string?)xml.Attribute("type"), "Spell", StringComparison.OrdinalIgnoreCase);
            command.CommandText = spell
                ? "SELECT sa.access_text FROM spell_access sa JOIN elements e ON e.element_id=sa.spell_element_id WHERE e.aurora_id=$id"
                : "SELECT es.support_text FROM element_supports es JOIN elements e ON e.element_id=es.element_id WHERE e.aurora_id=$id";
            var supports = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = command.ExecuteReader()) while (reader.Read()) supports.Add(reader.GetString(0));
            if (Program.SplitTopLevel(xml.Element("supports")?.Value ?? "", ',').Any(s => !supports.Contains(s)))
                throw new InvalidDataException($"Append support effects were lost while writing '{element.Id}'.");
            foreach (var (node, table) in new[] { ("grant", "grants"), ("select", "selects"), ("stat", "stats") })
            {
                var expected = xml.Element("rules")?.Elements(node).ToArray() ?? [];
                if (expected.Length == 0) continue;
                command.CommandText = $"SELECT r.raw_xml FROM {table} r JOIN rule_scopes rs ON rs.rule_scope_id=r.rule_scope_id JOIN elements e ON e.element_id=rs.owner_element_id WHERE e.aurora_id=$id AND rs.owner_kind='element'";
                var actual = new List<XElement>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) if (!reader.IsDBNull(0)) actual.Add(XElement.Parse(reader.GetString(0)));
                foreach (var rule in expected)
                {
                    int match = actual.FindIndex(r => XNode.DeepEquals(r, rule));
                    if (match < 0) throw new InvalidDataException($"Append {node} effects were lost while writing '{element.Id}'.");
                    actual.RemoveAt(match);
                }
            }
        }
    }

    internal static void WriteProvenance(SqliteConnection connection, ContentPreparation prepared)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS content_declaration_provenance (
              file_path TEXT NOT NULL, input_sha256 TEXT NOT NULL, effective_ordinal INTEGER NOT NULL,
              aurora_id TEXT NOT NULL, declaration_fingerprint TEXT NOT NULL, effective_declaration_xml TEXT NOT NULL,
              PRIMARY KEY(file_path,effective_ordinal));
            DELETE FROM content_declaration_provenance;
            CREATE TABLE IF NOT EXISTS content_preparation_metadata (
              singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), contract_version INTEGER NOT NULL,
              catalog_policy TEXT NOT NULL, append_policy TEXT NOT NULL);
            INSERT OR REPLACE INTO content_preparation_metadata VALUES (1,1,'unrestricted','materialized');
            CREATE TABLE IF NOT EXISTS content_prepared_elements (
              aurora_id TEXT PRIMARY KEY, file_path TEXT NOT NULL, base_xml TEXT NOT NULL, effective_xml TEXT NOT NULL);
            DELETE FROM content_prepared_elements;
            CREATE TABLE IF NOT EXISTS content_prepared_sources (
              aurora_id TEXT NOT NULL, file_path TEXT NOT NULL, relative_path TEXT NOT NULL,
              PRIMARY KEY(aurora_id,file_path));
            DELETE FROM content_prepared_sources;
            DROP VIEW IF EXISTS v_content_prepared_sources;
            CREATE VIEW v_content_prepared_sources AS
            SELECT s.*,cp.package_key,cp.package_kind,cp.package_name,cp.is_enabled
            FROM content_prepared_sources s JOIN source_files sf ON sf.relative_path=s.relative_path
            JOIN content_packages cp ON cp.content_package_id=sf.content_package_id;
            CREATE TABLE IF NOT EXISTS content_append_operations (
              file_path TEXT NOT NULL, relative_path TEXT NOT NULL, input_sha256 TEXT NOT NULL,
              ordinal INTEGER NOT NULL, target_aurora_id TEXT NOT NULL, operation_xml TEXT NOT NULL,
              status TEXT NOT NULL, diagnostic TEXT, PRIMARY KEY(file_path,ordinal));
            DELETE FROM content_append_operations;
            DROP VIEW IF EXISTS v_content_append_operations;
            CREATE VIEW v_content_append_operations AS
            SELECT a.*,cp.package_key,cp.package_kind,cp.package_name
            FROM content_append_operations a LEFT JOIN source_files sf ON sf.relative_path=a.relative_path
            LEFT JOIN content_packages cp ON cp.content_package_id=sf.content_package_id;
            """;
        command.ExecuteNonQuery();
        foreach (var d in prepared.Declarations)
        {
            command.CommandText = "INSERT INTO content_declaration_provenance VALUES ($path,$hash,$ordinal,$id,$fingerprint,$xml)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$path", d.Path);
            command.Parameters.AddWithValue("$hash", d.Hash);
            command.Parameters.AddWithValue("$ordinal", d.Ordinal);
            command.Parameters.AddWithValue("$id", d.Id);
            command.Parameters.AddWithValue("$fingerprint", d.Fingerprint);
            command.Parameters.AddWithValue("$xml", d.Xml);
            command.ExecuteNonQuery();
        }
        foreach (var e in prepared.Finalized)
        {
            command.CommandText = "INSERT INTO content_prepared_elements VALUES ($id,$path,$base,$effective)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", e.Id); command.Parameters.AddWithValue("$path", e.Path);
            command.Parameters.AddWithValue("$base", e.BaseXml); command.Parameters.AddWithValue("$effective", e.EffectiveXml);
            command.ExecuteNonQuery();
        }
        var relativePaths = prepared.Files.ToDictionary(f => f.Path, f => f.Relative, StringComparer.OrdinalIgnoreCase);
        foreach (var d in prepared.Declarations)
        {
            command.CommandText = "INSERT OR IGNORE INTO content_prepared_sources VALUES ($id,$path,$relative)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", d.Id); command.Parameters.AddWithValue("$path", d.Path);
            command.Parameters.AddWithValue("$relative", relativePaths[d.Path]); command.ExecuteNonQuery();
        }
        foreach (var a in prepared.Appends)
        {
            command.CommandText = "INSERT INTO content_append_operations VALUES ($path,$relative,$hash,$ordinal,$id,$xml,$status,$diagnostic)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$path", a.File.Path); command.Parameters.AddWithValue("$relative", a.File.Relative);
            command.Parameters.AddWithValue("$hash", a.File.Hash); command.Parameters.AddWithValue("$ordinal", a.Ordinal);
            command.Parameters.AddWithValue("$id", a.TargetId); command.Parameters.AddWithValue("$xml", a.Xml);
            command.Parameters.AddWithValue("$status", a.Status); command.Parameters.AddWithValue("$diagnostic", (object?)a.Diagnostic ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

}
