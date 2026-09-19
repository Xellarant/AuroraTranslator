#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace AuroraTranslator.Content;

public sealed record PreparedCatalogSource(string FilePath, string RelativePath, string PackageKey, string PackageKind);
public sealed record PreparedCatalogElement(string AuroraId, PreparedCatalogSource Source, string Xml);
public sealed record PreparedCatalogAppend(PreparedCatalogSource Source, int Ordinal, string TargetId, string Xml, string Status);
public sealed record PreparedCatalogProjection(IReadOnlyList<PreparedCatalogElement> Elements, IReadOnlyList<PreparedCatalogAppend> UnresolvedAppends);
public sealed record PreparedCatalogFile(PreparedCatalogSource Source, string Xml);

/// <summary>Optional app-side projections. Never mutates the complete catalog or consults saved enable flags.</summary>
public static class PreparedCatalogReader
{
    public static bool IsPrepared(SqliteConnection connection)
    {
        if (!HasPreparationMetadata(connection)) return false;
        using var q = connection.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM content_preparation_metadata WHERE singleton_id=1 AND contract_version=1 AND catalog_policy='unrestricted' AND append_policy='materialized'";
        return Convert.ToInt64(q.ExecuteScalar()) == 1;
    }

    public static bool HasPreparationMetadata(SqliteConnection connection)
    {
        using var q = connection.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_preparation_metadata'";
        return Convert.ToInt64(q.ExecuteScalar()) != 0;
    }

    public static bool InputsMatch(SqliteConnection connection, IEnumerable<string> contentRoots)
    {
        using var q = connection.CreateCommand();
        q.CommandText = "SELECT path,sha256 FROM local_correction_inputs";
        var recorded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = q.ExecuteReader()) while (reader.Read()) recorded.Add(reader.GetString(0), reader.GetString(1));
        var files = contentRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!files.SetEquals(recorded.Keys)) return false;
        return files.All(p => string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), recorded[p], StringComparison.Ordinal));
    }

    public static PreparedCatalogProjection Read(SqliteConnection connection,
        Func<PreparedCatalogSource, bool>? includeSource = null, Func<XElement, bool>? includeElement = null,
        IEnumerable<PreparedCatalogElement>? hostDefinitions = null,
        IReadOnlyList<PreparedCatalogFile>? runtimeFiles = null)
    {
        if (!IsPrepared(connection)) throw new InvalidDataException("A materialized, unrestricted content preparation snapshot is required.");
        includeSource ??= _ => true;
        runtimeFiles ??= [];
        var paths = runtimeFiles.Select(f => Path.GetFullPath(f.Source.FilePath)).ToHashSet(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        bool Replaced(PreparedCatalogSource source) => paths.Contains(Path.GetFullPath(source.FilePath));
        var suppliers = new Dictionary<string, PreparedCatalogSource>(StringComparer.Ordinal);
        using var q = connection.CreateCommand();
        q.CommandText = "SELECT aurora_id,file_path,relative_path,package_key,package_kind FROM v_content_prepared_sources ORDER BY relative_path,aurora_id";
        using (var reader = q.ExecuteReader()) while (reader.Read())
        {
            var source = new PreparedCatalogSource(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
            if (!Replaced(source) && includeSource(source)) suppliers.TryAdd(reader.GetString(0), source);
        }
        var definitions = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var catalogIds = new HashSet<string>(StringComparer.Ordinal);
        q.CommandText = "SELECT aurora_id,base_xml FROM content_prepared_elements ORDER BY aurora_id";
        using (var reader = q.ExecuteReader()) while (reader.Read())
        {
            var element = XElement.Parse(reader.GetString(1));
            catalogIds.Add(reader.GetString(0));
            if (suppliers.ContainsKey(reader.GetString(0)) && (includeElement?.Invoke(element) ?? true))
                definitions.Add(reader.GetString(0), element);
        }
        foreach (var file in runtimeFiles.Where(f => includeSource(f.Source)))
        {
            foreach (var xml in XElement.Parse(file.Xml).Elements("element"))
            {
                string id = (string?)xml.Attribute("id") ?? throw new InvalidDataException("Runtime element has no ID.");
                catalogIds.Add(id);
                if (!(includeElement?.Invoke(xml) ?? true)) continue;
                if (definitions.TryGetValue(id, out var previous))
                {
                    if (Builder.Data.Files.LocalCorrectionDocument.Fingerprint(previous) != Builder.Data.Files.LocalCorrectionDocument.Fingerprint(xml))
                        throw new InvalidDataException($"Conflicting runtime definitions for '{id}' in {file.Source.RelativePath}. Add explicit correction intent or distinct IDs.");
                    continue;
                }
                definitions.Add(id, new XElement(xml));
                suppliers[id] = file.Source;
            }
        }
        foreach (var host in hostDefinitions ?? [])
        {
            if (catalogIds.Contains(host.AuroraId) || !includeSource(host.Source)) continue;
            var xml = XElement.Parse(host.Xml);
            if ((string?)xml.Attribute("id") != host.AuroraId) throw new InvalidDataException("Host definition identity mismatch.");
            if (!(includeElement?.Invoke(xml) ?? true)) continue;
            if (definitions.TryGetValue(host.AuroraId, out var previous))
            {
                if (!XNode.DeepEquals(previous, xml)) throw new InvalidDataException($"Conflicting host definitions for '{host.AuroraId}'.");
                continue;
            }
            suppliers.Add(host.AuroraId, host.Source);
            definitions.Add(host.AuroraId, xml);
        }
        var unbound = new List<PreparedCatalogAppend>();
        var operations = new List<PreparedCatalogAppend>();
        q.CommandText = "SELECT file_path,relative_path,package_key,package_kind,ordinal,target_aurora_id,operation_xml,status FROM v_content_append_operations ORDER BY file_path,ordinal";
        using (var reader = q.ExecuteReader()) while (reader.Read())
        {
            var source = new PreparedCatalogSource(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
            if (Replaced(source) || !includeSource(source)) continue;
            var operation = new PreparedCatalogAppend(source, reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
            operations.Add(operation);
        }
        foreach (var file in runtimeFiles.Where(f => includeSource(f.Source)).OrderBy(f => f.Source.FilePath, StringComparer.Ordinal))
        {
            int ordinal = 0;
            foreach (var append in XElement.Parse(file.Xml).Elements("append"))
            {
                string id = (string?)append.Attribute("id") ?? throw new InvalidDataException("Runtime append has no target ID.");
                operations.Add(new(file.Source, ordinal, id, append.ToString(), "unresolved-target"));
                ordinal++;
            }
        }
        foreach (var operation in operations.OrderBy(o => o.Source.FilePath, StringComparer.Ordinal).ThenBy(o => o.Ordinal))
        {
            if (definitions.TryGetValue(operation.TargetId, out var definition))
                definitions[operation.TargetId] = ContentAppendComposer.Apply(definition, XElement.Parse(operation.Xml));
            else if (!catalogIds.Contains(operation.TargetId)) unbound.Add(operation);
        }
        return new(definitions.Select(e => new PreparedCatalogElement(e.Key, suppliers[e.Key], e.Value.ToString(SaveOptions.DisableFormatting))).ToArray(), unbound);
    }
}
