#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var files = new Dictionary<string, (string Root, string Xml)>(comparer);
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
                files.TryAdd(path, (roots[i], File.ReadAllText(path)));
            }
        }
        // When a managed local file is removed, restore its authoritative file at runtime.
        query.CommandText = "SELECT file_path,source_path FROM local_override_files WHERE status <> 'retired'";
        using (var reader = query.ExecuteReader()) while (reader.Read())
        {
            string local = reader.GetString(0);
            if (File.Exists(local)) continue;
            string origin = LocalCorrectionDocument.ResolveSourcePath(primaryRoot, reader.GetString(1));
            if (File.Exists(origin)) files[origin] = (primaryRoot, File.ReadAllText(origin));
        }
        var result = new Dictionary<string, PreparedCatalogFile>(comparer);
        var corrected = new HashSet<string>(comparer);
        var corrections = new List<LocalCorrection>();
        PreparedCatalogSource Source(string path, string root) => known.GetValueOrDefault(path)
            ?? new(path, Path.GetRelativePath(root, path), "runtime-xml", "homebrew");
        foreach (var (path, input) in files.OrderBy(f => f.Key, comparer))
        {
            var document = LocalCorrectionDocument.Parse(input.Xml);
            if (LocalCorrectionDocument.HasMetadata(input.Xml))
            {
                var evaluation = LocalCorrectionDocument.FromFile(path, input.Root)
                    ?? throw new InvalidDataException($"Cannot evaluate local correction {path}.");
                corrections.AddRange(evaluation.Corrections);
                string origin = LocalCorrectionDocument.ResolveSourcePath(input.Root, evaluation.SourcePath);
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
        return result.Values.ToArray();
    }
}
