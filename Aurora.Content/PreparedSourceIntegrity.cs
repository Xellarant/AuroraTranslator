#nullable enable
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Aurora.Content;

/// <summary>Checks captured inputs and normalized rows separately, without replaying an import.</summary>
internal static class PreparedSourceIntegrity
{
    private sealed record Header(string Path, string Id, string Name, string Type, string Source, string Status);
    private sealed record Profile(string Path, string Id, XElement Xml);
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool AppliesTo(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN "
            + "('content_preparation_metadata','content_prepared_elements','local_correction_inputs')";
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    internal static void Check(SqliteConnection connection, string? sourceRoot, List<string> failures)
    {
        try
        {
            if (!PreparedCatalogReader.IsPrepared(connection))
            {
                failures.Add("Content preparation snapshot is incomplete or incompatible. Refresh with ContentImport.ImportAsync.");
                return;
            }
            var expectedHeaders = new List<Header>();
            var expectedProfiles = new List<Profile>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path,sha256 FROM local_correction_inputs";
            var inputs = new Dictionary<string, string>(Paths);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) inputs.Add(Path.GetFullPath(reader.GetString(0)), reader.GetString(1));

            // Only the selected supplier owns the effective row; other identical suppliers
            // remain provenance and must not multiply normalized elements or spellcasting.
            command.CommandText = """
                SELECT p.aurora_id,p.file_path,p.effective_xml,s.relative_path
                FROM content_prepared_elements p
                LEFT JOIN content_prepared_sources s ON s.aurora_id=p.aurora_id AND s.file_path=p.file_path
                """;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string id = reader.GetString(0), path = reader.GetString(1);
                    if (reader.IsDBNull(3))
                    {
                        failures.Add($"Prepared source provenance is missing: {id}|{path}. Refresh the database.");
                        continue;
                    }
                    string relative = Relative(reader.GetString(3));
                    if (!inputs.ContainsKey(Path.GetFullPath(path)))
                        failures.Add("Prepared input fingerprint is missing: " + relative);
                    var xml = XElement.Parse(reader.GetString(2));
                    if (xml.Name != "element" || (string?)xml.Attribute("id") != id)
                        throw new InvalidDataException("Prepared declaration identity does not match its record: " + id);
                    expectedHeaders.Add(ReadHeader(relative, xml, "effective"));
                    if (xml.Element("spellcasting") is { } casting)
                        expectedProfiles.Add(new(relative, id, casting));
                }
            }

            // Rejected definitions have deliberately minimal rows: identity and status,
            // with no source-book attribution or active mechanics. Match the writer's
            // file/type eligibility, including definitions whose types were not imported.
            command.CommandText = "SELECT relative_path FROM source_files";
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) sourcePaths.Add(reader.GetString(0));
            command.CommandText = "SELECT type_name FROM element_types";
            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) types.Add(reader.GetString(0));
            command.CommandText = "SELECT relative_path,aurora_id,declaration_xml FROM content_rejected_declarations";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var xml = XElement.Parse(reader.GetString(2));
                    string type = (string?)xml.Attribute("type") ?? "";
                    if (sourcePaths.Contains(reader.GetString(0)) && types.Contains(type))
                        expectedHeaders.Add(new(Relative(reader.GetString(0)), reader.GetString(1),
                            (string?)xml.Attribute("name") ?? reader.GetString(1), type.ToLowerInvariant(), "", "superseded"));
                }
            }

            command.CommandText = """
                SELECT COALESCE(sf.relative_path,'[missing source]'),e.aurora_id,e.name,
                       COALESCE(et.type_name,''),COALESCE(sb.name,''),e.declaration_status
                FROM elements e LEFT JOIN source_files sf ON sf.source_file_id=e.source_file_id
                LEFT JOIN element_types et ON et.element_type_id=e.element_type_id
                LEFT JOIN source_books sb ON sb.source_book_id=e.source_book_id
                """;
            var actualHeaders = new List<Header>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) actualHeaders.Add(new(Relative(reader.GetString(0)), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3).ToLowerInvariant(), reader.GetString(4).ToLowerInvariant(), reader.GetString(5)));
            var expectedByPath = expectedHeaders.ToLookup(h => h.Path, StringComparer.Ordinal);
            var actualByPath = actualHeaders.ToLookup(h => h.Path, StringComparer.Ordinal);
            foreach (string path in expectedByPath.Select(g => g.Key).Union(actualByPath.Select(g => g.Key), StringComparer.Ordinal))
                if (!Order(expectedByPath[path]).SequenceEqual(Order(actualByPath[path])))
                    failures.Add("Prepared element identity mismatch (including declaration status): " + path);

            command.CommandText = """
                SELECT COALESCE(sf.relative_path,'[missing source]'),e.aurora_id,sp.raw_xml,e.declaration_status
                FROM spellcasting_profiles sp JOIN elements e ON e.element_id=sp.owner_element_id
                LEFT JOIN source_files sf ON sf.source_file_id=e.source_file_id
                """;
            var actualProfiles = new List<Profile>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    if (reader.GetString(3) != "effective")
                        failures.Add("Spellcasting owner has non-effective declaration status: " + reader.GetString(0) + "|" + reader.GetString(1));
                    if (reader.IsDBNull(2)) continue; // The existing normalized-profile check diagnoses missing XML.
                    actualProfiles.Add(new(Relative(reader.GetString(0)), reader.GetString(1), XElement.Parse(reader.GetString(2))));
                }
            var profilesByIdentity = actualProfiles.GroupBy(p => (p.Path, p.Id)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var expected in expectedProfiles)
            {
                profilesByIdentity.TryGetValue((expected.Path, expected.Id), out var matches);
                int index = matches?.FindIndex(p => XNode.DeepEquals(p.Xml, expected.Xml)) ?? -1;
                if (index < 0) failures.Add("Prepared spellcasting XML mismatch: " + expected.Path + "|" + expected.Id);
                else matches!.RemoveAt(index);
            }
            foreach (var extra in profilesByIdentity.Values.SelectMany(p => p))
                failures.Add("Unexpected prepared spellcasting profile: " + extra.Path + "|" + extra.Id);

            if (sourceRoot != null) CheckInputs(connection, sourceRoot, inputs, failures);
        }
        catch (Exception ex) when (ex is SqliteException or XmlException or InvalidDataException or ArgumentException)
        {
            failures.Add("Content preparation snapshot cannot be verified: " + ex.Message + " Refresh with ContentImport.ImportAsync.");
        }
    }

    private static void CheckInputs(SqliteConnection connection, string sourceRoot,
        Dictionary<string, string> inputs, List<string> failures)
    {
        string root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
        {
            failures.Add("Source directory unavailable: " + root);
            return;
        }
        foreach (var (path, hash) in inputs)
        {
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                failures.Add("Captured source is outside the requested root: " + path + ". Use the original root or refresh this database.");
                continue;
            }
            try
            {
                // Even known malformed/skipped XML can be byte-verified. Do not parse it or
                // treat the import's recorded recovery decisions as database corruption.
                using var stream = File.OpenRead(path);
                string actual = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(hash, actual, StringComparison.OrdinalIgnoreCase))
                    failures.Add("Source file hash mismatch: " + relative);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { failures.Add("Source file unavailable: " + relative); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failures.Add("Source file cannot be verified: " + relative + ". " + ex.Message); }
        }
        var retired = new HashSet<string>(Paths);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT file_path FROM local_override_files WHERE status='retired'";
            using var reader = command.ExecuteReader();
            while (reader.Read()) retired.Add(Path.GetFullPath(reader.GetString(0)));
        }
        try
        {
            foreach (string path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).Select(Path.GetFullPath))
                if (!inputs.ContainsKey(path))
                    failures.Add((retired.Contains(path) ? "Retired correction file is active again: " : "Source file was not captured by import: ")
                        + Path.GetRelativePath(root, path) + ". Refresh the database.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { failures.Add("Source directory cannot be fully inspected: " + root + ". " + ex.Message); }
    }

    private static Header ReadHeader(string path, XElement xml, string status) => new(path,
        (string?)xml.Attribute("id") ?? "", (string?)xml.Attribute("name") ?? "",
        ((string?)xml.Attribute("type") ?? "").ToLowerInvariant(), ((string?)xml.Attribute("source") ?? "").ToLowerInvariant(), status);
    private static string Relative(string path) => path.Replace('\\', '/');
    private static IEnumerable<Header> Order(IEnumerable<Header> rows) => rows
        .OrderBy(h => h.Id, StringComparer.Ordinal).ThenBy(h => h.Name, StringComparer.Ordinal)
        .ThenBy(h => h.Type, StringComparer.Ordinal).ThenBy(h => h.Source, StringComparer.Ordinal)
        .ThenBy(h => h.Status, StringComparer.Ordinal);
}
