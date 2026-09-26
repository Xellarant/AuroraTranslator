#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;

namespace Aurora.Content.Preparation;

/// <summary>Matches an imported duplicate decision to the declarations taking part in a runtime read.</summary>
internal sealed class RuntimeDefinitionDecisions
{
    private sealed record Declaration(int Ordinal, string Id, string Fingerprint);
    private readonly HashSet<string> changedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, int>> currentOrdinals;

    private RuntimeDefinitionDecisions(StringComparer paths) => currentOrdinals = new(paths);

    internal static RuntimeDefinitionDecisions Read(SqliteConnection connection,
        IReadOnlyList<PreparedCatalogFile> files, StringComparer paths)
    {
        var decisions = new RuntimeDefinitionDecisions(paths);
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_definition_resolutions'";
        if (Convert.ToInt64(query.ExecuteScalar()) == 0) return decisions;

        // Retained effective XML can depend on unreadable files and previously applied appends.
        // Its existing whole-revision recovery checks remain in RuntimeContentFiles.
        query.CommandText = "SELECT aurora_id FROM content_definition_resolutions WHERE kind IN ('provisional','upstream-successor')";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = query.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0));
        if (ids.Count == 0) return decisions;

        var recorded = new Dictionary<string, List<Declaration>>(paths);
        query.CommandText = """
            SELECT p.file_path,p.effective_ordinal,p.aurora_id,p.declaration_fingerprint
            FROM content_declaration_provenance p
            JOIN content_definition_resolutions r ON r.aurora_id=p.aurora_id
            WHERE r.kind IN ('provisional','upstream-successor')
            ORDER BY p.effective_ordinal
            """;
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            string path = Path.GetFullPath(reader.GetString(0));
            if (!recorded.TryGetValue(path, out var declarations)) recorded[path] = declarations = [];
            declarations.Add(new(reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        // Compare every participating file before removing anything. A changed winner or a new
        // competitor invalidates that ID's decision even when the rejected copy is unchanged.
        foreach (var file in files)
        {
            string path = Path.GetFullPath(file.Source.FilePath);
            var current = LocalCorrectionDocument.Parse(file.Xml, path).Root!.Elements("element")
                .Select((element, ordinal) => (Element: element, Ordinal: ordinal, Id: (string?)element.Attribute("id") ?? ""))
                .Where(e => ids.Contains(e.Id)).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var previous = (recorded.GetValueOrDefault(path) ?? [])
                .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var ordinals = decisions.currentOrdinals[path] = [];
            foreach (string id in current.Keys.Union(previous.Keys, StringComparer.Ordinal))
            {
                var before = previous.GetValueOrDefault(id) ?? [];
                var after = current.GetValueOrDefault(id) ?? [];
                // Ordering within one ID affects Legacy's winner. Unrelated definitions may move
                // or be inserted without changing this sequence, including absolute ordinals.
                if (!before.Select(d => d.Fingerprint).SequenceEqual(after.Select(e => LocalCorrectionDocument.Fingerprint(e.Element))))
                {
                    decisions.changedIds.Add(id);
                    continue;
                }
                for (int i = 0; i < before.Length; i++) ordinals.Add(before[i].Ordinal, after[i].Ordinal);
            }
        }
        return decisions;
    }

    internal bool TryMatch(string path, string id, int previousOrdinal, out int currentOrdinal)
    {
        currentOrdinal = -1;
        return !changedIds.Contains(id) && currentOrdinals.TryGetValue(path, out var ordinals)
            && ordinals.TryGetValue(previousOrdinal, out currentOrdinal);
    }
}
