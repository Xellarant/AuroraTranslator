#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

/// <summary>Read-only queries over a content database written by <see cref="ContentImport"/>.</summary>
public static class ContentDatabaseReader
{
    /// <summary>
    /// True when the database needs an import: it is missing, was not prepared by this library's
    /// current data version, or its recorded XML inputs differ from the files under the roots.
    /// </summary>
    public static bool IsStale(IReadOnlyList<string> contentRoots, string databasePath)
    {
        if (!File.Exists(databasePath)) return true;
        using (var connection = ContentDatabase.OpenReadableConnection(databasePath))
        {
            if (!PreparedCatalogReader.IsPrepared(connection)
                || ReadMetadata(connection)?.DataVersion != AuroraSqliteImporter.CurrentDataVersion)
                return true;
        }
        return LocalCorrectionSync.IsStale(contentRoots, databasePath) ?? true;
    }

    public static IReadOnlyList<LocalCorrectionStatus> ReadLocalCorrections(string databasePath) =>
        LocalCorrectionSync.ReadStatuses(databasePath);

    /// <summary>The effective content of a managed local correction file, or null when it is not managed.</summary>
    public static LocalCorrectionRuntimeContent? ReadLocalCorrectionContent(string filePath, string databasePath) =>
        LocalCorrectionSync.ReadRuntimeContent(filePath, databasePath);

    public static ContentDatabaseMetadata? ReadMetadata(string databasePath)
    {
        if (!File.Exists(databasePath)) return null;
        using var connection = ContentDatabase.OpenReadableConnection(databasePath);
        return ReadMetadata(connection);
    }

    private static ContentDatabaseMetadata? ReadMetadata(SqliteConnection connection)
    {
        using (var tableCheck = connection.CreateCommand())
        {
            tableCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='database_metadata';";
            if ((long)(tableCheck.ExecuteScalar() ?? 0L) == 0) return null;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT schema_version,
       data_version,
       importer_version,
       built_utc,
       source_file_count,
       element_count,
       content_root_hash
FROM database_metadata
WHERE singleton_id = 1;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ContentDatabaseMetadata(
            SchemaVersion: reader.GetInt32(0),
            DataVersion: reader.GetInt32(1),
            ImporterVersion: reader.GetString(2),
            BuiltUtc: reader.GetString(3),
            SourceFileCount: reader.GetInt32(4),
            ElementCount: reader.GetInt32(5),
            ContentRootHash: reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    public static ContentDatabaseHealthReport? ReadHealth(string databasePath)
    {
        if (!File.Exists(databasePath)) return null;

        using var conn = ContentDatabase.OpenReadableConnection(databasePath);

        int actionableUnresolvedLinks = ExecuteCount(
            conn,
            "SELECT COUNT(*) FROM v_unresolved_loader_link_diagnostics WHERE diagnostic_status = 'actionable';");
        int classifiedUnresolvedLinks = ExecuteCount(
            conn,
            "SELECT COUNT(*) FROM v_unresolved_loader_link_diagnostics WHERE diagnostic_status <> 'actionable';");
        int sourceIntegrityIssues = ExecuteCount(conn, "SELECT COUNT(*) FROM v_source_integrity_issues;");
        int missingResolvedSpellRows = ExecuteCount(conn, @"
SELECT COUNT(*)
FROM v_resolved_elements AS re
JOIN elements AS e
    ON e.element_id = re.winning_element_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
LEFT JOIN spells AS s
    ON s.element_id = e.element_id
WHERE et.type_name = 'Spell'
  AND s.element_id IS NULL;");
        int missingResolvedItemRows = ExecuteCount(conn, @"
SELECT COUNT(*)
FROM v_resolved_elements AS re
JOIN elements AS e
    ON e.element_id = re.winning_element_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
LEFT JOIN items AS i
    ON i.element_id = e.element_id
WHERE et.type_name = 'Item'
  AND i.element_id IS NULL;");
        int missingResolvedCompanionRows = ExecuteCount(conn, @"
SELECT COUNT(*)
FROM v_resolved_elements AS re
JOIN elements AS e
    ON e.element_id = re.winning_element_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
LEFT JOIN companions AS c
    ON c.element_id = e.element_id
WHERE et.type_name = 'Companion'
  AND c.element_id IS NULL;");

        return new ContentDatabaseHealthReport(
            actionableUnresolvedLinks,
            classifiedUnresolvedLinks,
            sourceIntegrityIssues,
            missingResolvedSpellRows,
            missingResolvedItemRows,
            missingResolvedCompanionRows,
            QueryHealthIssueGroups(conn),
            QueryHealthIssueSamples(conn));
    }

    private static IReadOnlyList<ContentDatabaseHealthIssueGroup> QueryHealthIssueGroups(SqliteConnection connection)
    {
        var groups = new List<ContentDatabaseHealthIssueGroup>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    'unresolved-link' AS area,
    diagnostic_status AS status,
    COALESCE(diagnostic_reason, 'actionable') AS reason,
    link_kind AS kind,
    '' AS file_path,
    COUNT(*) AS issue_count
FROM v_unresolved_loader_link_diagnostics
GROUP BY diagnostic_status, diagnostic_reason, link_kind
ORDER BY
    CASE diagnostic_status
        WHEN 'actionable' THEN 0
        WHEN 'runtime-resource' THEN 1
        WHEN 'option-pool' THEN 2
        ELSE 3
    END,
    issue_count DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                groups.Add(ReadHealthIssueGroup(reader));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    'source-integrity' AS area,
    'review' AS status,
    issue_kind AS reason,
    COALESCE(owner_type_name, '') AS kind,
    relative_path AS file_path,
    COUNT(*) AS issue_count
FROM v_source_integrity_issues
GROUP BY issue_kind, owner_type_name, relative_path
ORDER BY issue_count DESC, issue_kind, relative_path
LIMIT 24;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                groups.Add(ReadHealthIssueGroup(reader));
        }

        return groups;
    }

    private static IReadOnlyList<ContentDatabaseHealthIssueSample> QueryHealthIssueSamples(SqliteConnection connection)
    {
        var samples = new List<ContentDatabaseHealthIssueSample>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    'unresolved-link' AS area,
    diagnostic_status AS status,
    COALESCE(diagnostic_reason, 'actionable') AS reason,
    link_kind AS kind,
    '' AS file_path,
    COALESCE(owner_aurora_id, owner_name, '') AS owner,
    COALESCE(unresolved_key, '') AS issue_key,
    COALESCE(unresolved_text, '') AS issue_text
FROM v_unresolved_loader_link_diagnostics
WHERE diagnostic_status = 'actionable'
ORDER BY link_kind, owner_type_name, owner_name
LIMIT 12;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                samples.Add(ReadHealthIssueSample(reader));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    'source-integrity' AS area,
    'review' AS status,
    issue_kind AS reason,
    COALESCE(owner_type_name, '') AS kind,
    relative_path AS file_path,
    COALESCE(owner_aurora_id, owner_name, '') AS owner,
    COALESCE(issue_key, '') AS issue_key,
    COALESCE(issue_text, '') AS issue_text
FROM v_source_integrity_issues
ORDER BY issue_kind, relative_path, owner_name
LIMIT 12;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                samples.Add(ReadHealthIssueSample(reader));
        }

        return samples;
    }

    private static int ExecuteCount(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static ContentDatabaseHealthIssueGroup ReadHealthIssueGroup(SqliteDataReader reader) =>
        new(
            ClassifyTrustImpact(ReadString(reader, 0), ReadString(reader, 1), ReadString(reader, 2)),
            ReadString(reader, 0),
            ReadString(reader, 1),
            ReadString(reader, 2),
            ReadString(reader, 3),
            ReadString(reader, 4),
            Convert.ToInt32(reader.GetValue(5)));

    private static ContentDatabaseHealthIssueSample ReadHealthIssueSample(SqliteDataReader reader) =>
        new(
            ClassifyTrustImpact(ReadString(reader, 0), ReadString(reader, 1), ReadString(reader, 2)),
            ReadString(reader, 0),
            ReadString(reader, 1),
            ReadString(reader, 2),
            ReadString(reader, 3),
            ReadString(reader, 4),
            ReadString(reader, 5),
            ReadString(reader, 6),
            ReadString(reader, 7));

    private static string ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static ContentDatabaseTrustImpact ClassifyTrustImpact(string area, string status, string reason)
    {
        if (string.Equals(area, "unresolved-link", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(status, "actionable", StringComparison.OrdinalIgnoreCase)
                ? ContentDatabaseTrustImpact.Blocking
                : ContentDatabaseTrustImpact.Expected;
        }

        if (!string.Equals(area, "source-integrity", StringComparison.OrdinalIgnoreCase))
            return ContentDatabaseTrustImpact.ManualReview;

        return reason switch
        {
            "grant-target-id-in-name-attribute" => ContentDatabaseTrustImpact.AutoRecovered,
            "recovered-grant-target-id-in-name-attribute" => ContentDatabaseTrustImpact.AutoRecovered,
            "duplicate-element-signature-in-file" => ContentDatabaseTrustImpact.AutoRecovered,
            "duplicate-element-id-in-file" => ContentDatabaseTrustImpact.ManualReview,
            "blank-grant-target-id" => ContentDatabaseTrustImpact.ManualReview,
            "blank-select-type" => ContentDatabaseTrustImpact.ManualReview,
            "blank-stat-name" => ContentDatabaseTrustImpact.ManualReview,
            _ => ContentDatabaseTrustImpact.ManualReview
        };
    }
}
