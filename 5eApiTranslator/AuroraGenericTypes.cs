using AuroraTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AuroraTranslator;

internal static partial class AuroraSqliteImporter
{
    // Unknown authored types are data, not permission to discard an element or
    // reinterpret its type. The generic path stores shared text/support/rule data.
    private static HashSet<string> RegisterGenericTypes(SqliteConnection connection,
        SqliteTransaction transaction, AuroraImportCatalog catalog, Dictionary<string, long> types)
    {
        var reimport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in catalog.Elements.Where(e => !string.IsNullOrWhiteSpace(e.type))
                     .GroupBy(e => e.type, StringComparer.OrdinalIgnoreCase))
        {
            if (types.ContainsKey(group.Key)) continue;
            ExecuteInsert(connection, transaction,
                "INSERT INTO element_types(type_name,loader_family) VALUES ($type,'generic-unrecognized')",
                ("$type", group.Key));
            types.Add(group.Key, GetLastInsertRowId(connection, transaction));
            // Old imports could have cached a file after silently omitting this
            // type. Reimport its declarations even when that file's hash matches.
            reimport.UnionWith(group.Select(e => e.source_file_path ?? ""));
        }
        ExecuteSql(connection, transaction, """
            CREATE VIEW IF NOT EXISTS v_unrecognized_element_type_diagnostics AS
            SELECT e.aurora_id,e.name,et.type_name,sf.relative_path,
                   'unrecognized-element-type' AS diagnostic_status,
                   'Preserved as a generic record. Review the authored type or add explicit app support; no type-specific semantics were inferred.' AS diagnostic_reason
            FROM elements e JOIN element_types et ON et.element_type_id=e.element_type_id
            LEFT JOIN source_files sf ON sf.source_file_id=e.source_file_id
            WHERE et.loader_family='generic-unrecognized';
            """);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type_name FROM element_types WHERE loader_family='generic-unrecognized'";
        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader()) while (reader.Read()) generic.Add(reader.GetString(0));
        foreach (var e in catalog.Elements.Where(e => generic.Contains(e.type)))
            Console.Error.WriteLine($"unrecognized-element-type: '{e.id}', type '{e.type}', {e.source_file_path}. Preserved as a generic record; review the XML type or explicit app support.");
        return reimport;
    }
}
