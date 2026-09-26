#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;

namespace Aurora.Content.Preparation;

/// <summary>XML inputs outside the persisted primary catalog, plus current local corrections.</summary>
public static class RuntimeContentFiles
{
    public static IReadOnlyList<PreparedCatalogFile> Read(SqliteConnection connection, string primaryRoot,
        IEnumerable<string> secondaryRoots, bool includePrimary = false)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var known = new Dictionary<string, PreparedCatalogSource>(comparer);
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT file_path, relative_path, package_key, package_kind FROM v_content_prepared_sources
            UNION SELECT file_path, relative_path, package_key, package_kind FROM v_content_append_operations
            """;
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            var source = new PreparedCatalogSource(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
            known.TryAdd(Path.GetFullPath(source.FilePath), source);
        }
        var skipped = ReadSkippedFiles(connection, comparer);
        var skippedAppends = ReadSkippedAppends(connection, comparer);
        var files = new Dictionary<string, RuntimeInput>(comparer);
        var roots = new[] { primaryRoot }.Concat(secondaryRoots).Select(Path.GetFullPath).Distinct(comparer).ToArray();
        for (int i = 0; i < roots.Length; i++)
        {
            string scan = i == 0 && !includePrimary ? Path.Combine(roots[i], "user") : roots[i];
            if (!Directory.Exists(scan)) continue;
            foreach (string file in Directory.EnumerateFiles(scan, "*.xml", SearchOption.AllDirectories))
            {
                string path = Path.GetFullPath(file);
                // An overlapping secondary root must not reimport primary content.
                string rel = Path.GetRelativePath(roots[0], path);
                if (i > 0 && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(rel)) continue;
                RuntimeInput input;
                try { input = Capture(path, roots[i]); }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                    skipped.TryGetValue(path, out string? uncaptured) && uncaptured == ContentInputFingerprint.Unreadable)
                { continue; }
                // Only the rejected revision is excluded. A local repair must be usable without
                // waiting for another database refresh.
                if (skipped.TryGetValue(path, out string? rejectedHash) && rejectedHash == input.Sha256) continue;
                files.TryAdd(path, input);
            }
        }
        // When a managed local file is removed, restore its authoritative file at runtime.
        query.CommandText = "SELECT file_path,source_path FROM local_override_files WHERE status <> 'retired'";
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            string local = reader.GetString(0);
            if (File.Exists(local)) continue;
            string origin = LocalCorrectionDocument.ResolveSourcePath(primaryRoot, reader.GetString(1));
            if (File.Exists(origin)) files[origin] = Capture(origin, primaryRoot);
        }
        var revisions = files.ToDictionary(f => f.Key, f => f.Value.Sha256, comparer);
        var result = new Dictionary<string, PreparedCatalogFile>(comparer);
        var corrected = new HashSet<string>(comparer);
        var corrections = new List<LocalCorrection>();
        PreparedCatalogSource Source(string path, string root) => known.GetValueOrDefault(path)
            ?? new(path, Path.GetRelativePath(root, path), "runtime-xml", "homebrew");
        foreach (var (path, input) in files.OrderBy(f => f.Key, comparer))
        {
            var document = LocalCorrectionDocument.Parse(input.Xml, path);
            if (LocalCorrectionDocument.HasMetadata(input.Xml))
            {
                string relative = Path.GetRelativePath(input.Root, path).Replace('\\', '/');
                if (!relative.StartsWith("user/local/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Correction metadata is only active inside user/local.");
                var section = document.Root!.Elements().Single(e => e.Name.LocalName == "corrections");
                string origin = LocalCorrectionDocument.ResolveSourcePath(input.Root, (string?)section.Attribute("source-path") ?? "");
                var upstream = Capture(origin, input.Root);
                revisions[origin] = upstream.Sha256;
                var evaluation = LocalCorrectionDocument.Evaluate(input.Xml, upstream.Xml);
                corrections.AddRange(evaluation.Corrections);
                if (!corrected.Add(origin)) throw new InvalidDataException($"Multiple local corrections target {origin}.");
                // Match Translator preparation: a new local definition belongs to
                // its local supplier; replacements keep the authoritative supplier.
                var effective = LocalCorrectionDocument.Parse(evaluation.EffectiveXml);
                var local = LocalCorrectionDocument.Parse(evaluation.LocalXml);
                local.Root!.Elements().Where(e => e.Name != "info").Remove();
                var additions = evaluation.Corrections.Where(c => c.Operation == "add" && c.State == "review-pending")
                    .Select(c => c.TargetId).ToHashSet(StringComparer.Ordinal);
                var upstreamIds = LocalCorrectionDocument.Parse(evaluation.UpstreamXml).Root!.Elements("element")
                    .Select(e => (string?)e.Attribute("id")).ToHashSet(StringComparer.Ordinal);
                foreach (var element in effective.Root!.Elements("element")
                    .Where(e => additions.Contains((string?)e.Attribute("id") ?? "") && !upstreamIds.Contains((string?)e.Attribute("id"))).ToArray())
                {
                    element.Remove();
                    local.Root.Add(element);
                }
                result[origin] = new(Source(origin, input.Root), effective.ToString(SaveOptions.DisableFormatting));
                result[path] = new(Source(path, input.Root), local.ToString(SaveOptions.DisableFormatting));
            }
            else if (!corrected.Contains(path))
                result[path] = new(Source(path, input.Root),
                    (bool?)document.Root!.Attribute("ignore") == true ? "<elements />" : input.Xml);
        }
        if (corrections.Where(c => c.Group != null).GroupBy(c => c.Group)
            .Any(g => g.Select(c => c.State).Distinct().Count() > 1))
            throw new InvalidDataException("Related corrections across files must be reviewed together.");
        return result.Values.Select(file => ApplyDefinitionDecisions(connection,
            RemoveSkippedAppends(file, revisions, skippedAppends), revisions)).ToArray();
    }

    private sealed record RuntimeInput(string Root, string Xml, string Sha256);
    private sealed record SkippedAppend(string Sha256, int Ordinal, string Xml);

    private static RuntimeInput Capture(string path, string root)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes));
        return new(root, reader.ReadToEnd(), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Files the last import left out whole. An append skip is not one of them: that file was
    /// imported, only one of its operations was dropped.
    /// </summary>
    private static Dictionary<string, string> ReadSkippedFiles(SqliteConnection connection, StringComparer comparer)
    {
        var skipped = new Dictionary<string, string>(comparer);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_skipped_files'";
        if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 0) return skipped;
        command.CommandText = "SELECT s.file_path,i.sha256 FROM content_skipped_files s JOIN local_correction_inputs i ON i.path=s.file_path WHERE s.kind='unreadable'";
        using var reader = command.ExecuteReader();
        while (reader.Read()) skipped[Path.GetFullPath(reader.GetString(0))] = reader.GetString(1);
        return skipped;
    }

    private static Dictionary<string, List<SkippedAppend>> ReadSkippedAppends(SqliteConnection connection, StringComparer comparer)
    {
        var skipped = new Dictionary<string, List<SkippedAppend>>(comparer);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_append_operations'";
        if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 0) return skipped;
        command.CommandText = "SELECT file_path,input_sha256,ordinal,operation_xml FROM content_append_operations WHERE status='skipped'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string path = Path.GetFullPath(reader.GetString(0));
            if (!skipped.TryGetValue(path, out var operations)) skipped[path] = operations = [];
            operations.Add(new(reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        }
        return skipped;
    }

    private static PreparedCatalogFile RemoveSkippedAppends(PreparedCatalogFile file,
        Dictionary<string, string> revisions, Dictionary<string, List<SkippedAppend>> skipped)
    {
        string path = Path.GetFullPath(file.Source.FilePath);
        if (!revisions.TryGetValue(path, out string? revision) || !skipped.TryGetValue(path, out var operations)) return file;
        var matches = operations.Where(operation => operation.Sha256 == revision).ToArray();
        if (matches.Length == 0) return file;
        var document = LocalCorrectionDocument.Parse(file.Xml, path);
        var appends = document.Root!.Elements("append").ToArray();
        foreach (var operation in matches)
            if (operation.Ordinal >= 0 && operation.Ordinal < appends.Length &&
                XNode.DeepEquals(appends[operation.Ordinal], XElement.Parse(operation.Xml, LoadOptions.PreserveWhitespace)))
                appends[operation.Ordinal].Remove();
        return file with { Xml = document.ToString(SaveOptions.DisableFormatting) };
    }

    private static PreparedCatalogFile ApplyDefinitionDecisions(SqliteConnection connection,
        PreparedCatalogFile file, IReadOnlyDictionary<string, string> revisions)
    {
        string path = Path.GetFullPath(file.Source.FilePath);
        if (!revisions.TryGetValue(path, out string? revision)) return file;
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='content_rejected_declarations'";
        if (Convert.ToInt64(query.ExecuteScalar()) == 0) return file;
        var document = LocalCorrectionDocument.Parse(file.Xml, path);
        var elements = document.Root!.Elements("element").ToArray();
        query.CommandText = "SELECT ordinal,aurora_id,declaration_xml FROM content_rejected_declarations WHERE file_path=$path AND input_sha256=$hash";
        query.Parameters.AddWithValue("$path", path); query.Parameters.AddWithValue("$hash", revision);
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            int ordinal = reader.GetInt32(0);
            if (ordinal < elements.Length && (string?)elements[ordinal].Attribute("id") == reader.GetString(1) &&
                LocalCorrectionDocument.Fingerprint(elements[ordinal]) == LocalCorrectionDocument.Fingerprint(XElement.Parse(reader.GetString(2))))
                elements[ordinal].Remove();
        }
        query.CommandText = "SELECT ordinal,aurora_id,selected_xml FROM content_definition_resolutions WHERE selected_file_path=$path AND input_sha256=$hash AND kind='retained'";
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            int ordinal = reader.GetInt32(0);
            if (ordinal < elements.Length && elements[ordinal].Parent != null && (string?)elements[ordinal].Attribute("id") == reader.GetString(1))
                elements[ordinal].ReplaceWith(XElement.Parse(reader.GetString(2)));
        }
        return file with { Xml = document.ToString(SaveOptions.DisableFormatting) };
    }
}
