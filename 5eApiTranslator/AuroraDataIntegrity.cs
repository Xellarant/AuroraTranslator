using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace AuroraTranslator;

internal static class AuroraDataIntegrity
{
    private sealed record ElementHeader(string Id, string Name, string Type, string Source);
    internal static List<string> Check(string sqlitePath, string sourceRoot = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        var failures = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(0) != "ok") failures.Add("SQLite: " + reader.GetString(0));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                failures.Add($"Foreign key: {reader.GetString(0)} row {reader.GetValue(1)} -> {reader.GetString(2)}");
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT data_version, element_count, source_file_count FROM database_metadata WHERE singleton_id = 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                failures.Add("Missing database metadata.");
                return failures;
            }
            if (reader.GetInt32(0) != AuroraSqliteImporter.CurrentDataVersion)
            {
                failures.Add($"Data version {reader.GetInt32(0)} requires refresh to {AuroraSqliteImporter.CurrentDataVersion}.");
                return failures;
            }
            int elements = reader.GetInt32(1), files = reader.GetInt32(2);
            reader.Close();
            command.CommandText = "SELECT COUNT(*) FROM elements;";
            if (Convert.ToInt64(command.ExecuteScalar()) != elements) failures.Add("Metadata element count mismatch.");
            command.CommandText = "SELECT COUNT(*) FROM source_files;";
            if (Convert.ToInt64(command.ExecuteScalar()) != files) failures.Add("Metadata source file count mismatch.");
        }

        var profiles = new List<(long Id, string Path, string AuroraId, string Raw, string List, string Extend,
            string Name, string Ability, bool Extended, bool? Prepare, bool? Replace, bool? All)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT sp.spellcasting_profile_id, sf.relative_path, e.aurora_id, sp.raw_xml, sp.list_text, sp.extend_text,
       sp.profile_name, sp.ability_name, sp.is_extended, sp.prepare_spells, sp.allow_replace, sp.assign_to_all
FROM spellcasting_profiles sp JOIN elements e ON e.element_id = sp.owner_element_id
JOIN source_files sf ON sf.source_file_id = e.source_file_id;
""";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                profiles.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    Text(reader, 3), Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7),
                    reader.GetBoolean(8), Boolean(reader, 9), Boolean(reader, 10), Boolean(reader, 11)));
        }
        foreach (var profile in profiles)
        {
            string label = profile.Path + "|" + profile.AuroraId;
            if (profile.Raw == null)
            {
                failures.Add("Spellcasting requires XML reimport: " + label);
                continue;
            }
            try
            {
                XElement xml = XElement.Parse(profile.Raw);
                var children = xml.Elements().Where(child => child.Name == "list" || child.Name == "extend").ToList();
                var expected = children.Select((child, index) => new AuroraSqliteImporter.SpellcastingEntryRow(
                    child.Name.LocalName, index + 1, child.Value, Flag(child, "known") ?? false,
                    child.ToString(SaveOptions.DisableFormatting)))
                    .OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.Ordinal);
                if (!expected.SequenceEqual(AuroraSqliteImporter.ReadSpellcastingEntries(connection, profile.Id)))
                    failures.Add("Spellcasting entry mismatch: " + label);
                if (profile.Name != ((string)xml.Attribute("name") ?? "N/A")
                    || profile.Ability != (string)xml.Attribute("ability")
                    || profile.Extended != (Flag(xml, "extend") ?? false)
                    || profile.Prepare != Flag(xml, "prepare") || profile.Replace != Flag(xml, "allowReplace")
                    || profile.All != (Flag(xml, "all") ?? false)
                    || profile.List != children.LastOrDefault(child => child.Name == "list")?.Value
                    || profile.Extend != string.Join(",", children.Where(child => child.Name == "extend").Select(child => child.Value)))
                    failures.Add("Spellcasting profile mismatch: " + label);
            }
            catch (System.Xml.XmlException error)
            {
                failures.Add("Invalid spellcasting XML: " + label + ": " + error.Message);
            }
        }
        if (sourceRoot != null)
        {
            var sourceFiles = new List<(string Path, string Hash)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT relative_path, file_hash FROM source_files;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) sourceFiles.Add((reader.GetString(0), Text(reader, 1)));
            }
            foreach (var (relativePath, hash) in sourceFiles)
            {
                string path = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
                string root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                {
                    failures.Add("Source file unavailable: " + relativePath);
                    continue;
                }
                using (var stream = File.OpenRead(path))
                {
                    if (!string.Equals(hash, Convert.ToHexString(MD5.HashData(stream)), StringComparison.OrdinalIgnoreCase))
                        failures.Add("Source file hash mismatch: " + relativePath);
                }
                var sourceElements = XDocument.Load(path).Root?.Elements("element")
                    .Where(element => !string.IsNullOrWhiteSpace((string)element.Attribute("id"))
                        && !string.IsNullOrWhiteSpace((string)element.Attribute("type"))).ToList() ?? new();
                var expectedHeaders = sourceElements.Select(element => new ElementHeader(
                    (string)element.Attribute("id"), (string)element.Attribute("name") ?? "",
                    ((string)element.Attribute("type")).ToLowerInvariant(), ((string)element.Attribute("source") ?? "").ToLowerInvariant()));
                var actualHeaders = new List<ElementHeader>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
SELECT e.aurora_id, e.name, et.type_name, COALESCE(sb.name, '')
FROM elements e JOIN source_files sf ON sf.source_file_id = e.source_file_id
JOIN element_types et ON et.element_type_id = e.element_type_id
LEFT JOIN source_books sb ON sb.source_book_id = e.source_book_id
WHERE sf.relative_path = $path;
""";
                    command.Parameters.AddWithValue("$path", relativePath);
                    using var reader = command.ExecuteReader();
                    while (reader.Read()) actualHeaders.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2).ToLowerInvariant(), reader.GetString(3).ToLowerInvariant()));
                }
                if (!OrderHeaders(expectedHeaders).SequenceEqual(OrderHeaders(actualHeaders)))
                    failures.Add("Source element identity mismatch: " + relativePath);
                var expected = sourceElements
                    .Where(element => element.Element("spellcasting") != null)
                    .Select(element => ((string)element.Attribute("id"), Xml: element.Element("spellcasting")))
                    .ToList() ?? new();
                var actual = profiles.Where(profile => profile.Path == relativePath).ToList();
                if (actual.Count != expected.Count)
                    failures.Add("Source spellcasting profile count mismatch: " + relativePath);
                foreach (var (id, xml) in expected)
                {
                    if (!actual.Any(profile => profile.AuroraId == id && profile.Raw != null
                        && XNode.DeepEquals(XElement.Parse(profile.Raw), xml)))
                        failures.Add("Source spellcasting XML mismatch: " + relativePath + "|" + id);
                }
            }
        }
        return failures;
    }

    private static string Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static IEnumerable<ElementHeader> OrderHeaders(IEnumerable<ElementHeader> headers)
        => headers.OrderBy(header => header.Id, StringComparer.Ordinal).ThenBy(header => header.Name, StringComparer.Ordinal)
            .ThenBy(header => header.Type, StringComparer.Ordinal).ThenBy(header => header.Source, StringComparer.Ordinal);
    private static bool? Boolean(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetBoolean(index);
    private static bool? Flag(XElement element, string name)
        => bool.TryParse((string)element.Attribute(name), out bool value) ? value : null;
}
