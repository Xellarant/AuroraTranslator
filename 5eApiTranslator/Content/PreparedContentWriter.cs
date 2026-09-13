#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuroraTranslator.Content;

/// <summary>Validates the candidate against finalized declarations and persists their provenance.</summary>
internal static class PreparedContentWriter
{
    internal static void ValidatePackageAvailabilityUpdate(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_declaration_provenance'";
        if (Convert.ToInt64(command.ExecuteScalar()) == 0) return;
        command.CommandText = "SELECT COUNT(*) FROM (SELECT aurora_id FROM content_declaration_provenance GROUP BY aurora_id HAVING COUNT(DISTINCT file_path)>1)";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            throw new InvalidDataException("Database-only package availability changes are not supported for consolidated suppliers. A preparation-aware package workflow must select the eligible supplier before refreshing this database.");
    }

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
            LEFT JOIN source_files sf ON sf.source_file_id=e.source_file_id
            LEFT JOIN content_packages cp ON cp.content_package_id=sf.content_package_id
            LEFT JOIN resolved_elements_cache r ON r.aurora_id=e.aurora_id
            WHERE (COALESCE(cp.is_enabled,1)=1 AND (r.aurora_id IS NULL OR r.winning_element_id<>e.element_id))
               OR (COALESCE(cp.is_enabled,1)=0 AND r.aurora_id IS NOT NULL)
            """;
        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            throw new InvalidDataException("Finalized elements do not match package availability in the candidate resolution cache.");
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
        transaction.Commit();
    }

}
