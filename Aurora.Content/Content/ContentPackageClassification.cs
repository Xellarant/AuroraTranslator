#nullable enable
using Aurora.Content.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aurora.Content.Preparation;

internal static class ContentPackageClassification
{
    internal static string Root(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "local";

    internal static string FromPath(string path) => Root(path) switch
    {
        "core" => "core",
        "official" or "unearthed-arcana" or "ua" => "official", // Authorship, not final/published rules status.
        "third-party" or "thirdparty" or "supplements" or "supplemental" => "third-party",
        _ => "homebrew"
    };

    private static bool HasExplicitLocalRoot(string path) => Root(path) is "user" or "local" or "homebrew" or "reddit" or "dndwiki";
    private static bool HasPublishedSupplementRoot(string path) => Root(path) is "third-party" or "thirdparty" or "supplements" or "supplemental";

    // Reuses the Source setter vocabulary already used by the Lights importer.
    // A copied official Source declaration cannot turn an explicit local override
    // into first-party content. A repository URL is not a publisher classification.
    private static string? FromSource(AuroraElement source)
    {
        var setters = source.setters;
        var categories = new List<string>();
        if (setters?.GetBoolean("core") == true || setters?.GetBoolean("official") == true) categories.Add("official");
        if (setters?.GetBoolean("third-party") == true) categories.Add("third-party");
        if (setters?.GetBoolean("homebrew") == true) categories.Add("homebrew");
        if (categories.Count > 1)
            throw new InvalidDataException($"Conflicting Source classifications for '{source.id}' in {source.source_file_path}: {string.Join(", ", categories)}. Correct the publisher flags before importing.");
        if (setters?.GetBoolean("core") == true) return "core";
        if (setters?.GetBoolean("official") == true) return "official";
        if (setters?.GetBoolean("third-party") == true) return "third-party";
        if (setters?.GetBoolean("homebrew") == true) return "homebrew";
        if (string.Equals(setters?.GetValue("author"), "Wizards of the Coast", StringComparison.OrdinalIgnoreCase)) return "official";
        return setters?.GetBoolean("supplement") == true ? "third-party" : null;
    }

    internal sealed record FileClassification(string Kind, string PackageKey, string LegacyPackageKey);

    internal static IReadOnlyDictionary<string, FileClassification> ForCatalog(AuroraImportCatalog catalog,
        bool skipUnusable = false, Action<string, string>? diagnostic = null)
    {
        var ambiguousFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? ClassifySource(AuroraElement source)
        {
            try { return FromSource(source); }
            catch (InvalidDataException ex) when (skipUnusable)
            {
                ambiguousFiles.Add(source.source_file_path ?? "");
                diagnostic?.Invoke(source.source_file_path ?? "", ex.Message + " Imported without a publisher classification; review the metadata.");
                return null;
            }
        }
        var declarations = catalog.Elements.Where(e => e.type.Equals("Source", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(e.name) && !HasExplicitLocalRoot(e.source_file_path ?? ""))
            .Select(e => (Name: e.name, Path: e.source_file_path ?? "", Kind: ClassifySource(e)))
            .Where(e => e.Kind != null).ToArray();
        var namesByFile = catalog.Elements.Select(e => (Path: e.source_file_path ?? "",
                Name: e.type.Equals("Source", StringComparison.OrdinalIgnoreCase) ? e.name : e.source))
            .Concat(catalog.Spells.Select(e => (Path: e.source_file_path ?? "", Name: e.source)))
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var kindsByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in catalog.Files)
        {
            string path = file.RelativePath, key = AuroraSqliteImporter.GetContentPackageKey(path);
            string kind = FromPath(path);
            if (!HasExplicitLocalRoot(path) && Root(path) is not ("core" or "official" or "unearthed-arcana" or "ua"))
            {
                var packageSources = declarations.Where(d => AuroraSqliteImporter.GetContentPackageKey(d.Path) == key).ToArray();
                namesByFile.TryGetValue(path, out var names);
                // Prefer this file's Source declaration, then a referenced publication
                // declared by this collection. External book citations do not change
                // the supplier of a package with an unambiguous own Source declaration.
                var evidence = packageSources.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (evidence.Length == 0) evidence = packageSources.Where(d => names?.Contains(d.Name) == true).ToArray();
                if (evidence.Length == 0)
                {
                    // An aggregate index is a container, not a publisher. Inherit only
                    // from the nearest containing directory that declares Sources.
                    string normalized = path.Replace('\\', '/');
                    var nearby = packageSources.Where(d => normalized.StartsWith(
                        (System.IO.Path.GetDirectoryName(d.Path)?.Replace('\\', '/') ?? "") + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (nearby.Length > 0)
                    {
                        int depth = nearby.Max(d => d.Path.Count(c => c is '/' or '\\'));
                        evidence = nearby.Where(d => d.Path.Count(c => c is '/' or '\\') == depth).ToArray();
                    }
                }
                if (evidence.Length == 0) evidence = declarations.Where(d => names?.Contains(d.Name) == true).ToArray();
                // Older Source files also call published DMs Guild material
                // "homebrew". In a published supplement bucket that flag means
                // unofficial authorship; the user's category is third-party.
                var categories = evidence.Select(d => d.Kind == "core" ? "official" :
                    d.Kind == "homebrew" && HasPublishedSupplementRoot(path) ? "third-party" : d.Kind!).Distinct().ToArray();
                if (categories.Length > 1)
                {
                    string message = $"Conflicting Source classifications in '{path}': {string.Join(", ", categories)}. Supply unambiguous Source metadata or separate the mixed file; no publisher was inferred.";
                    if (!skipUnusable) throw new InvalidDataException(message);
                    kind = "local";
                    diagnostic?.Invoke(path, message + " The content was imported without a publisher classification.");
                }
                if (categories.Length == 1) kind = categories[0];
                if (categories.Length == 0 && !HasPublishedSupplementRoot(path) && !HasExplicitLocalRoot(path))
                    kind = "local";
            }
            if (ambiguousFiles.Contains(path)) kind = "local";
            kindsByFile.Add(path, kind);
        }
        // A collection may ship first-party books beside homebrew. Split only
        // these mixed buckets; keep every existing homogeneous package key stable.
        var mixed = catalog.Files.GroupBy(f => AuroraSqliteImporter.GetContentPackageKey(f.RelativePath))
            .Where(g => g.Select(f => kindsByFile[f.RelativePath]).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return kindsByFile.ToDictionary(pair => pair.Key, pair =>
        {
            string legacy = AuroraSqliteImporter.GetContentPackageKey(pair.Key);
            return new FileClassification(pair.Value, mixed.Contains(legacy) ? legacy + "--" + pair.Value : legacy, legacy);
        }, StringComparer.OrdinalIgnoreCase);
    }
}
