#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

/// <summary>
/// Reads the complete imported catalog, without source preferences or live XML overlays.
/// Each call reads a consistent SQLite snapshot. Only current, prepared databases are supported.
/// </summary>
public static class ContentCatalogReader
{
    /// <summary>All effective IDs, including hidden/internal types, ordered by ordinal Aurora ID.</summary>
    public static ContentCatalogSnapshot ReadSummaries(string databasePath) => Read(databasePath, session =>
    {
        using var command = session.Command(SummarySql + " ORDER BY e.aurora_id COLLATE BINARY");
        using var reader = command.ExecuteReader();
        var entries = new List<ContentCatalogSummary>();
        while (reader.Read()) entries.Add(ReadSummary(reader));
        return new ContentCatalogSnapshot(session.Metadata, entries.AsReadOnly());
    });

    /// <summary>Looks up an exact ID, then a stored forwarding alias. Unknown IDs return null.</summary>
    public static ContentCatalogDetail? ReadDetail(string databasePath, string auroraId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auroraId);
        return Read(databasePath, session =>
        {
            var (summary, alias) = session.Resolve(auroraId);
            if (summary == null) return null;
            string description = "";
            string? markup = null, xml = null;
            using (var command = session.Command("""
                SELECT COALESCE(t.body,''),m.raw_xml,p.effective_xml
                FROM resolved_elements_cache r
                LEFT JOIN element_texts t ON t.element_id=r.winning_element_id AND t.text_kind='description' AND t.ordinal=1
                LEFT JOIN element_text_markup m ON m.element_text_id=t.element_text_id
                LEFT JOIN content_prepared_elements p ON p.aurora_id=r.aurora_id
                WHERE r.aurora_id=$id
                """, summary.AuroraId))
            using (var reader = command.ExecuteReader())
                if (reader.Read()) { description = reader.GetString(0); markup = Text(reader, 1); xml = Text(reader, 2); }

            var suppliers = new List<ContentCatalogSource>();
            using (var command = session.Command("""
                SELECT relative_path,package_key,package_name,package_kind
                FROM v_content_prepared_sources WHERE aurora_id=$id ORDER BY relative_path COLLATE BINARY
                """, summary.AuroraId))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) suppliers.Add(ReadSource(reader, 0));

            var appends = new List<ContentCatalogAppend>();
            using (var command = session.Command("""
                SELECT relative_path,package_key,package_name,package_kind,ordinal,operation_xml
                FROM v_content_append_operations WHERE target_aurora_id=$id AND status='applied'
                ORDER BY file_path COLLATE BINARY,ordinal
                """, summary.AuroraId))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) appends.Add(new(ReadSource(reader, 0), reader.GetInt32(4), reader.GetString(5)));

            return new ContentCatalogDetail(session.Metadata, auroraId, alias, summary, description, markup, xml,
                suppliers.AsReadOnly(), appends.AsReadOnly(), ReadLinks(session, summary.Type, xml));
        });
    }

    private static IReadOnlyList<ContentCatalogLink> ReadLinks(Session session, string type, string? xml)
    {
        var links = new List<ContentCatalogLink>();
        if (xml == null) return links.AsReadOnly();
        var element = XElement.Parse(xml);
        void Add(string kind, string id, string? expectedType, string? ruleXml)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim(); // Reference padding is not part of a declared identity.
            var (target, alias) = session.Resolve(id);
            var status = target == null ? ContentCatalogLinkStatus.MissingTarget
                : !string.IsNullOrWhiteSpace(expectedType) && !string.Equals(target.Type, expectedType, StringComparison.OrdinalIgnoreCase)
                    ? ContentCatalogLinkStatus.TypeMismatch : ContentCatalogLinkStatus.Resolved;
            links.Add(new(kind, id, expectedType, ruleXml, status, target, alias));
        }
        // Preserve conditions and duplicate grants; resolving an ID does not activate a rule.
        foreach (var grant in element.Elements("rules").Elements("grant"))
            Add("grant", (string?)grant.Attribute("id") ?? "", (string?)grant.Attribute("type"), grant.ToString(SaveOptions.DisableFormatting));
        if (string.Equals(type, "Companion", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var setter in element.Elements("setters").Elements("set"))
            {
                string kind = ((string?)setter.Attribute("name") ?? "").ToLowerInvariant();
                string? expected = kind switch
                {
                    "traits" => "Companion Trait", "actions" => "Companion Action", "reactions" => "Companion Reaction", _ => null
                };
                if (expected != null)
                    foreach (string id in setter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        Add(kind, id, expected, null);
            }
        }
        return links.AsReadOnly();
    }

    // Joins use the effective element row, never a name or all rows sharing an Aurora ID.
    // No package enable flag, rank, or compendium presentation policy filters the catalog.
    private const string SummarySql = """
        SELECT e.aurora_id,e.name,et.type_name,sb.name,e.compendium_display,
               COALESCE(summary.body,description.body,''),sf.relative_path,cp.package_key,cp.package_name,cp.package_kind,
               sp.spell_level,sp.school_name,sp.is_ritual,sp.is_concentration,res.kind,
               (SELECT COUNT(*) FROM elements other WHERE other.aurora_id=e.aurora_id AND other.declaration_status='superseded')
        FROM resolved_elements_cache r
        JOIN elements e ON e.element_id=r.winning_element_id AND e.declaration_status='effective'
        JOIN element_types et ON et.element_type_id=e.element_type_id
        LEFT JOIN source_books sb ON sb.source_book_id=e.source_book_id
        LEFT JOIN source_files sf ON sf.source_file_id=e.source_file_id
        LEFT JOIN content_packages cp ON cp.content_package_id=sf.content_package_id
        LEFT JOIN element_texts summary ON summary.element_id=e.element_id AND summary.text_kind='summary' AND summary.ordinal=1
        LEFT JOIN element_texts description ON description.element_id=e.element_id AND description.text_kind='description' AND description.ordinal=1
        LEFT JOIN spells sp ON sp.element_id=e.element_id
        LEFT JOIN content_definition_resolutions res ON res.aurora_id=e.aurora_id
        WHERE NOT EXISTS (SELECT 1 FROM content_unavailable_elements u WHERE u.aurora_id=e.aurora_id)
        """;

    private static ContentCatalogSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), Text(reader, 3), reader.GetBoolean(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : ReadSource(reader, 6),
        reader.IsDBNull(10) ? null : new(reader.GetInt32(10), Text(reader, 11), reader.GetBoolean(12), reader.GetBoolean(13)),
        Text(reader, 14), reader.GetInt32(15));

    private static ContentCatalogSource ReadSource(SqliteDataReader reader, int offset) => new(
        reader.GetString(offset), Text(reader, offset + 1), Text(reader, offset + 2), Text(reader, offset + 3));

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static T Read<T>(string databasePath, Func<Session, T> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Import content before reading the catalog.", databasePath);
        try
        {
            using var connection = ContentDatabase.OpenReadableConnection(databasePath);
            using var transaction = connection.BeginTransaction(deferred: true);
            var session = new Session(connection, transaction);
            return read(session);
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException($"Cannot read the prepared catalog at '{databasePath}'. Verify the database is accessible and refresh it with ContentImport.ImportAsync. {ex.Message}", ex);
        }
    }

    private sealed class Session
    {
        private readonly SqliteConnection connection;
        private readonly SqliteTransaction transaction;
        private readonly Dictionary<string, (ContentCatalogSummary? Summary, ContentCatalogAlias? Alias)> resolved = new(StringComparer.Ordinal);
        internal ContentDatabaseMetadata Metadata { get; }

        internal Session(SqliteConnection connection, SqliteTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
            using var command = Command("""
                SELECT m.schema_version,m.data_version,m.importer_version,m.built_utc,m.source_file_count,m.element_count,m.content_root_hash,
                       p.contract_version,p.catalog_policy,p.append_policy
                FROM database_metadata m LEFT JOIN content_preparation_metadata p ON p.singleton_id=m.singleton_id
                WHERE m.singleton_id=1
                """);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != ContentDatabaseReader.CurrentSchemaVersion
                || reader.GetInt32(1) != ContentDatabaseReader.CurrentDataVersion || reader.IsDBNull(7)
                || reader.GetInt32(7) != 1 || Text(reader, 8) != "unrestricted" || Text(reader, 9) != "materialized")
                throw new InvalidDataException($"Catalog reads require a prepared database with schema {ContentDatabaseReader.CurrentSchemaVersion}, data {ContentDatabaseReader.CurrentDataVersion}. Refresh with ContentImport.ImportAsync.");
            Metadata = new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), Text(reader, 6));
        }

        internal SqliteCommand Command(string sql, string? id = null)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            if (id != null) command.Parameters.AddWithValue("$id", id);
            return command;
        }

        private ContentCatalogSummary? Find(string id)
        {
            using var command = Command(SummarySql + " AND e.aurora_id=$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadSummary(reader) : null;
        }

        internal (ContentCatalogSummary? Summary, ContentCatalogAlias? Alias) Resolve(string id)
        {
            if (resolved.TryGetValue(id, out var cached)) return cached;
            var summary = Find(id);
            ContentCatalogAlias? alias = null;
            if (summary == null)
            {
                // A real declaration or deliberately unavailable ID must never be revived by forwarding.
                using var command = Command("""
                    SELECT saved_aurora_id,target_aurora_id,origin,note FROM content_element_aliases
                    WHERE saved_aurora_id=$id
                      AND NOT EXISTS (SELECT 1 FROM elements WHERE aurora_id=$id)
                      AND NOT EXISTS (SELECT 1 FROM content_unavailable_elements WHERE aurora_id=$id)
                    """, id);
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) alias = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), Text(reader, 3));
                if (alias != null) summary = Find(alias.TargetId); // Validated aliases point directly to live IDs.
            }
            return resolved[id] = (summary, alias);
        }
    }
}
