using _5eApiTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace _5eApiTranslator
{
    internal static class AuroraSqlitePocImporter
    {
        /// <summary>
        /// Incrementally imports Aurora XML catalog into the SQLite database.
        /// Files whose MD5 hash matches the stored hash are skipped; changed or
        /// new files are deleted (cascade) and re-imported. Deleted files are
        /// removed automatically.  <paramref name="srdJsonPath"/> is optional —
        /// when provided the SRD monsters are also imported/updated if their
        /// file has changed.
        /// </summary>
        public static void Import(
            AuroraImportCatalog catalog,
            string schemaPath,
            string sqlitePath,
            string srdJsonPath = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath) ?? AppContext.BaseDirectory);

            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString());
            connection.Open();

            // Apply schema if it has not been applied yet (idempotent: IF NOT EXISTS guards).
            EnsureSchema(connection, schemaPath);

            // The schema SQL sets PRAGMA foreign_keys = ON for standalone use; reaffirm it here
            // so enforcement is active for the import transaction too.
            ExecuteSql(connection, null, "PRAGMA foreign_keys = ON;");

            using var transaction = connection.BeginTransaction();

            Dictionary<string, long> elementTypeIds = LoadElementTypeIds(connection, transaction);

            // ── Source books: accumulate-only (INSERT OR IGNORE) ────────────────
            var sourceBookIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceName in catalog.Elements.Select(x => x.source)
                .Concat(catalog.Spells.Select(x => x.source))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sourceBookIds[sourceName] = EnsureSourceBook(connection, transaction, sourceName);
            }

            // ── Source files: incremental (hash-checked) ─────────────────────────
            var existingFiles = LoadExistingSourceFileHashes(connection, transaction);
            var sourceFileIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var changedPaths  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPaths     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in catalog.Files)
            {
                seenPaths.Add(file.RelativePath);
                string hash = ComputeFileHash(file.FullPath);

                if (existingFiles.TryGetValue(file.RelativePath, out var existing))
                {
                    if (existing.Hash == hash)
                    {
                        // Unchanged — reuse existing ID, skip element re-import.
                        sourceFileIds[file.RelativePath] = existing.Id;
                        continue;
                    }
                    // Changed — delete cascade, then re-import below.
                    DeleteSourceFile(connection, transaction, existing.Id);
                }

                long newId = InsertSourceFile(connection, transaction, file, hash);
                sourceFileIds[file.RelativePath] = newId;
                changedPaths.Add(file.RelativePath);
            }

            // Remove source files that are no longer on disk (cascade cleans elements).
            foreach (var (path, existing) in existingFiles)
            {
                if (!seenPaths.Contains(path))
                    DeleteSourceFile(connection, transaction, existing.Id);
            }

            int addedElements = 0;

            // ── Elements: only process changed/new files ─────────────────────────
            foreach (var element in catalog.Elements)
            {
                if (!changedPaths.Contains(element.source_file_path ?? string.Empty)) continue;
                if (!elementTypeIds.TryGetValue(element.type, out long elementTypeId)) continue;

                long elementId = InsertElementBase(
                    connection, transaction, elementTypeId,
                    sourceBookIds.TryGetValue(element.source ?? string.Empty, out var sbId) ? sbId : (long?)null,
                    sourceFileIds.TryGetValue(element.source_file_path ?? string.Empty, out var sfId) ? sfId : (long?)null,
                    element.id, element.name, element.index,
                    element.compendium.display, DetermineLoaderPriority(element.type));

                InsertElementTexts(connection, transaction, elementId, element);
                InsertElementSupports(connection, transaction, elementId, element.supports);
                InsertElementRequirements(connection, transaction, elementId, element.requirements);
                InsertElementBlocks(connection, transaction, elementId, element.additionalBlocks);
                InsertSetters(connection, transaction, elementId, "element", element.setters);
                InsertExtract(connection, transaction, elementId, element.extract);

                if (element.spellcasting != null)
                    InsertSpellcastingProfile(connection, transaction, elementId, element.type, element.spellcasting);

                InsertSubtypeRecord(connection, transaction, elementId, element);
                InsertRules(connection, transaction, elementId, "element", element.rules);

                if (string.Equals(element.type, "class", StringComparison.OrdinalIgnoreCase)
                    && element.multiclass != null)
                {
                    InsertClassMulticlass(connection, transaction, elementId, element.multiclass);
                    InsertSetters(connection, transaction, elementId, "class-multiclass", element.multiclass.setters);
                    InsertRules(connection, transaction, elementId, "class-multiclass", element.multiclass.rules);
                }

                addedElements++;
            }

            // ── Spells: only process changed/new files ───────────────────────────
            foreach (var spell in catalog.Spells)
            {
                if (!changedPaths.Contains(spell.source_file_path ?? string.Empty)) continue;
                if (!elementTypeIds.TryGetValue("Spell", out long elementTypeId)) continue;

                long elementId = InsertElementBase(
                    connection, transaction, elementTypeId,
                    sourceBookIds.TryGetValue(spell.source ?? string.Empty, out var sbId) ? sbId : (long?)null,
                    sourceFileIds.TryGetValue(spell.source_file_path ?? string.Empty, out var sfId) ? sfId : (long?)null,
                    spell.aurora_id, spell.name, spell.index,
                    spell.compendium_display, DetermineLoaderPriority("Spell"));

                InsertSpellTexts(connection, transaction, elementId, spell);
                InsertSetters(connection, transaction, elementId, "element", spell.setters);
                InsertSpellRecord(connection, transaction, elementId, spell);
                addedElements++;
            }

            // ── SRD creatures: import/update if JSON file changed ────────────────
            int srdAdded = 0;
            if (!string.IsNullOrEmpty(srdJsonPath) && File.Exists(srdJsonPath))
                srdAdded = ImportSrdCreaturesIfChanged(connection, transaction, srdJsonPath);

            // Only re-resolve cross-file FK relationships when something actually changed.
            if (changedPaths.Count > 0 || srdAdded > 0)
            {
                ResolveDeferredRelationships(connection, transaction);
                RebuildExpressionCatalog(connection, transaction);
            }

            transaction.Commit();

            int skipped = catalog.Files.Count - changedPaths.Count;
            Console.WriteLine(
                $"Aurora import: {addedElements} elements processed " +
                $"({changedPaths.Count} files changed, {skipped} unchanged).");
            if (srdAdded > 0)
                Console.WriteLine($"SRD creatures: {srdAdded} creatures imported/updated.");
            else if (!string.IsNullOrEmpty(srdJsonPath))
                Console.WriteLine("SRD creatures: no changes.");
        }

        // ── Schema / DB setup ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the schema SQL to the database. All DDL uses <c>IF NOT EXISTS</c> /
        /// <c>INSERT OR IGNORE</c> guards, making this safe to re-run on an existing DB.
        /// Running it on every open also picks up new tables added to the schema after
        /// a database was first created. Then runs <see cref="ApplyMigrations"/> for any
        /// changes (ADD COLUMN) that cannot be expressed with IF NOT EXISTS.
        /// </summary>
        private static void EnsureSchema(SqliteConnection connection, string schemaPath)
        {
            // Always run the schema SQL — all DDL uses IF NOT EXISTS / INSERT OR IGNORE guards,
            // making it safe to re-run against an existing database. This also handles the case
            // where new tables were added to the schema after the DB was initially created.
            //
            // The .sql file contains PRAGMA / BEGIN TRANSACTION / COMMIT for use with standalone
            // SQLite tools.  Strip those lines before executing programmatically: we manage our
            // own transaction and deliberately leave FK enforcement OFF during bulk import.
            string rawSql = File.ReadAllText(schemaPath);
            string schemaSql = System.Text.RegularExpressions.Regex.Replace(
                rawSql,
                @"^\s*(PRAGMA\s+\S.*?;|BEGIN\s+TRANSACTION\s*;|COMMIT\s*;|ROLLBACK\s*;)\s*$",
                "",
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            using var schema = connection.CreateCommand();
            schema.CommandText = schemaSql;
            schema.ExecuteNonQuery();

            // Apply any migrations that can't be expressed with IF NOT EXISTS in the schema
            // (e.g. ADD COLUMN on an existing table).
            ApplyMigrations(connection);
        }

        /// <summary>
        /// Applies incremental schema migrations for columns that cannot be added via
        /// <c>IF NOT EXISTS</c> in the schema SQL (SQLite does not support conditional ADD COLUMN).
        /// All new tables are handled by the schema SQL itself; only column additions go here.
        /// </summary>
        private static void ApplyMigrations(SqliteConnection connection)
        {
            // M001: add file_hash to source_files (added for incremental import support).
            // New databases get this column from the schema SQL; this migration handles
            // databases that were created before the column existed.
            using var colCheck = connection.CreateCommand();
            colCheck.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('source_files') WHERE name = 'file_hash';";
            if ((long)colCheck.ExecuteScalar() == 0)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE source_files ADD COLUMN file_hash TEXT;";
                alter.ExecuteNonQuery();
            }
        }

        // ── Source book helpers ──────────────────────────────────────────────────

        /// <summary>Inserts the source book if it doesn't exist and returns its ID.</summary>
        private static long EnsureSourceBook(
            SqliteConnection connection, SqliteTransaction transaction, string sourceName)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO source_books (name) VALUES ($name);";
            insert.Parameters.AddWithValue("$name", sourceName);
            insert.ExecuteNonQuery();

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT source_book_id FROM source_books WHERE name = $name;";
            select.Parameters.AddWithValue("$name", sourceName);
            return (long)select.ExecuteScalar();
        }

        // ── Source file helpers ──────────────────────────────────────────────────

        private static Dictionary<string, (long Id, string Hash)> LoadExistingSourceFileHashes(
            SqliteConnection connection, SqliteTransaction transaction)
        {
            var map = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT source_file_id, relative_path, file_hash FROM source_files;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(1)] = (reader.GetInt64(0), reader.IsDBNull(2) ? "" : reader.GetString(2));
            return map;
        }

        private static void DeleteSourceFile(
            SqliteConnection connection, SqliteTransaction transaction, long sourceFileId)
        {
            // ON DELETE CASCADE on elements.source_file_id handles all element child tables.
            // Cross-file nullable FKs (features.parent_element_id, grants.target_element_id, etc.)
            // all have ON DELETE SET NULL, so the DB engine nulls them automatically.
            // ResolveDeferredRelationships at the end of Import() re-resolves them from text IDs.
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM source_files WHERE source_file_id = $id;";
            cmd.Parameters.AddWithValue("$id", sourceFileId);
            cmd.ExecuteNonQuery();
        }

        private static string ComputeFileHash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using var md5    = MD5.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(md5.ComputeHash(stream));
        }

        private static Dictionary<string, long> LoadElementTypeIds(SqliteConnection connection, SqliteTransaction transaction)
        {
            Dictionary<string, long> map = new(StringComparer.OrdinalIgnoreCase);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT element_type_id, type_name FROM element_types;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(1)] = reader.GetInt64(0);
            }

            return map;
        }

        private static long InsertSourceFile(
            SqliteConnection connection, SqliteTransaction transaction,
            AuroraFileInfo file, string hash = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO source_files
(
    relative_path,
    package_name,
    package_description,
    version_text,
    update_file_name,
    update_url,
    author_name,
    author_url,
    file_hash
)
VALUES
(
    $relative_path,
    $package_name,
    $package_description,
    $version_text,
    $update_file_name,
    $update_url,
    $author_name,
    $author_url,
    $file_hash
);";

            command.Parameters.AddWithValue("$relative_path",       file.RelativePath);
            command.Parameters.AddWithValue("$package_name",        (object)file.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("$package_description", (object)file.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$version_text",        (object)file.FileVersion?.versionString ?? DBNull.Value);
            command.Parameters.AddWithValue("$update_file_name",    (object)file.FileVersion?.fileName ?? DBNull.Value);
            command.Parameters.AddWithValue("$update_url",          (object)file.FileVersion?.fileUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$author_name",         (object)file.Author?.name ?? DBNull.Value);
            command.Parameters.AddWithValue("$author_url",          (object)file.Author?.url ?? DBNull.Value);
            command.Parameters.AddWithValue("$file_hash",           (object)hash ?? DBNull.Value);
            command.ExecuteNonQuery();

            return GetLastInsertRowId(connection, transaction);
        }

        private static long InsertElementBase(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long elementTypeId,
            long? sourceBookId,
            long? sourceFileId,
            string auroraId,
            string name,
            string slug,
            bool compendiumDisplay,
            int loaderPriority)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO elements
(
    aurora_id,
    element_type_id,
    source_book_id,
    source_file_id,
    name,
    slug,
    compendium_display,
    loader_priority
)
VALUES
(
    $aurora_id,
    $element_type_id,
    $source_book_id,
    $source_file_id,
    $name,
    $slug,
    $compendium_display,
    $loader_priority
);";

            command.Parameters.AddWithValue("$aurora_id", auroraId);
            command.Parameters.AddWithValue("$element_type_id", elementTypeId);
            command.Parameters.AddWithValue("$source_book_id", sourceBookId.HasValue ? sourceBookId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$source_file_id", sourceFileId.HasValue ? sourceFileId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$slug", slug ?? name?.Trim().ToLower().Replace(" ", "-"));
            command.Parameters.AddWithValue("$compendium_display", compendiumDisplay ? 1 : 0);
            command.Parameters.AddWithValue("$loader_priority", loaderPriority);
            command.ExecuteNonQuery();

            return GetLastInsertRowId(connection, transaction);
        }

        private static void InsertElementTexts(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraElement element)
        {
            if (!string.IsNullOrWhiteSpace(element.prerequisite))
            {
                InsertElementText(connection, transaction, elementId, "prerequisite", 1, null, null, null, null, null, element.prerequisite);
            }

            if (element.prerequisites?.Any() == true)
            {
                int ordinal = 1;
                foreach (var prerequisite in element.prerequisites)
                {
                    InsertElementText(connection, transaction, elementId, "prerequisites", ordinal++, null, null, null, null, null, prerequisite);
                }
            }

            if (!string.IsNullOrWhiteSpace(element.description))
            {
                InsertElementText(connection, transaction, elementId, "description", 1, null, null, null, null, null, element.description, element.descriptionRawXml);
            }

            if (element.sheet == null)
                return;

            if (element.sheet.description?.Any() == true)
            {
                int ordinal = 1;

                foreach (var sheetDescription in element.sheet.description)
                {
                    InsertElementText(
                        connection,
                        transaction,
                        elementId,
                        "sheet",
                        ordinal++,
                        sheetDescription.level,
                        element.sheet.display,
                        element.sheet.alt,
                        element.sheet.action,
                        element.sheet.usage,
                        sheetDescription.text,
                        sheetDescription.rawXml);
                }
            }
            else
            {
                InsertElementText(
                    connection,
                    transaction,
                    elementId,
                    "sheet",
                    1,
                    null,
                    element.sheet.display,
                    element.sheet.alt,
                    element.sheet.action,
                    element.sheet.usage,
                    string.Empty,
                    element.sheet.rawXml);
            }
        }

        private static void InsertSpellTexts(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraSpell spell)
        {
            if (spell.desc?.Any() == true)
            {
                InsertElementText(connection, transaction, elementId, "description", 1, null, null, null, null, null, string.Join(Environment.NewLine, spell.desc), spell.descriptionRawXml);
            }

            if (spell.higher_level?.Any() == true)
            {
                InsertElementText(connection, transaction, elementId, "summary", 1, null, null, null, null, null, string.Join(Environment.NewLine, spell.higher_level));
            }
        }

        private static void InsertElementText(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long elementId,
            string textKind,
            int ordinal,
            int? level,
            bool? display,
            string altText,
            string actionText,
            string usageText,
            string body,
            string rawXml = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO element_texts
(
    element_id,
    text_kind,
    ordinal,
    level,
    display,
    alt_text,
    action_text,
    usage_text,
    body
)
VALUES
(
    $element_id,
    $text_kind,
    $ordinal,
    $level,
    $display,
    $alt_text,
    $action_text,
    $usage_text,
    $body
);";

            command.Parameters.AddWithValue("$element_id", elementId);
            command.Parameters.AddWithValue("$text_kind", textKind);
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$level", level.HasValue ? level.Value : DBNull.Value);
            command.Parameters.AddWithValue("$display", display.HasValue ? (display.Value ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$alt_text", (object)altText ?? DBNull.Value);
            command.Parameters.AddWithValue("$action_text", (object)actionText ?? DBNull.Value);
            command.Parameters.AddWithValue("$usage_text", (object)usageText ?? DBNull.Value);
            command.Parameters.AddWithValue("$body", body ?? string.Empty);
            command.ExecuteNonQuery();

            if (!string.IsNullOrWhiteSpace(rawXml))
            {
                long elementTextId = GetLastInsertRowId(connection, transaction);
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO element_text_markup
(element_text_id, content_format, raw_xml)
VALUES
($element_text_id, 'aurora-xml', $raw_xml);",
                    ("$element_text_id", elementTextId),
                    ("$raw_xml", rawXml));
            }
        }

        private static void InsertElementBlocks(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long elementId,
            IEnumerable<AuroraBlockEntry> blocks)
        {
            if (blocks?.Any() != true)
                return;

            int ordinal = 1;
            foreach (var block in blocks)
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO element_blocks
(element_id, ordinal, block_name, body_text, raw_xml)
VALUES
($element_id, $ordinal, $block_name, $body_text, $raw_xml);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$block_name", block.name ?? string.Empty),
                    ("$body_text", (object)block.value ?? DBNull.Value),
                    ("$raw_xml", block.rawXml ?? string.Empty));

                long elementBlockId = GetLastInsertRowId(connection, transaction);
                int attributeOrdinal = 1;
                foreach (var attribute in block.attributes)
                {
                    ExecuteInsert(connection, transaction,
                        @"INSERT INTO element_block_attributes
(element_block_id, ordinal, attribute_name, attribute_value)
VALUES
($element_block_id, $ordinal, $attribute_name, $attribute_value);",
                        ("$element_block_id", elementBlockId),
                        ("$ordinal", attributeOrdinal++),
                        ("$attribute_name", attribute.Key),
                        ("$attribute_value", (object)attribute.Value ?? DBNull.Value));
                }
            }
        }

        private static void InsertElementSupports(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraTextCollection supports)
        {
            if (supports == null || supports.Count == 0)
                return;

            int ordinal = 1;

            foreach (var support in supports)
            {
                ExecuteInsert(
                    connection,
                    transaction,
                    "INSERT INTO element_supports (element_id, ordinal, support_text) VALUES ($element_id, $ordinal, $support_text);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$support_text", support));
            }
        }

        private static void InsertElementRequirements(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraTextCollection requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return;

            int ordinal = 1;

            foreach (var requirement in requirements)
            {
                ExecuteInsert(
                    connection,
                    transaction,
                    "INSERT INTO element_requirements (element_id, ordinal, requirement_text) VALUES ($element_id, $ordinal, $requirement_text);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$requirement_text", requirement));
            }
        }

        private static void InsertSubtypeRecord(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraElement element)
        {
            if (string.Equals(element.type, "Source", StringComparison.OrdinalIgnoreCase))
            {
                var authorSetter = element.setters?.FindEntry("author");

                ExecuteInsert(connection, transaction,
                    @"INSERT INTO source_elements
(element_id, abbreviation_text, source_url, image_url, errata_url, author_name, author_abbreviation, author_url, is_official, is_core, is_supplement, is_third_party, release_text)
VALUES
($element_id, $abbreviation_text, $source_url, $image_url, $errata_url, $author_name, $author_abbreviation, $author_url, $is_official, $is_core, $is_supplement, $is_third_party, $release_text);",
                    ("$element_id", elementId),
                    ("$abbreviation_text", (object)element.setters?.GetValue("abbreviation") ?? DBNull.Value),
                    ("$source_url", (object)element.setters?.GetValue("url") ?? DBNull.Value),
                    ("$image_url", (object)element.setters?.GetValue("image") ?? DBNull.Value),
                    ("$errata_url", (object)element.setters?.GetValue("errata") ?? DBNull.Value),
                    ("$author_name", (object)authorSetter?.value ?? DBNull.Value),
                    ("$author_abbreviation", (object)authorSetter?.GetAttribute("abbreviation") ?? DBNull.Value),
                    ("$author_url", (object)authorSetter?.GetAttribute("url") ?? DBNull.Value),
                    ("$is_official", element.setters?.GetBoolean("official").HasValue == true ? (element.setters.GetBoolean("official").Value ? 1 : 0) : DBNull.Value),
                    ("$is_core", element.setters?.GetBoolean("core").HasValue == true ? (element.setters.GetBoolean("core").Value ? 1 : 0) : DBNull.Value),
                    ("$is_supplement", element.setters?.GetBoolean("supplement").HasValue == true ? (element.setters.GetBoolean("supplement").Value ? 1 : 0) : DBNull.Value),
                    ("$is_third_party", element.setters?.GetBoolean("third-party").HasValue == true ? (element.setters.GetBoolean("third-party").Value ? 1 : 0) : DBNull.Value),
                    ("$release_text", (object)element.setters?.GetValue("release") ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Class", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO classes (element_id, hit_die, short_text) VALUES ($element_id, $hit_die, $short_text);",
                    ("$element_id", elementId),
                    ("$hit_die", (object)element.setters?.hd ?? DBNull.Value),
                    ("$short_text", (object)element.setters?.@short ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Archetype", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO archetypes (element_id, parent_support_text) VALUES ($element_id, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Race", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO races (element_id, names_format_text) VALUES ($element_id, $names_format_text);",
                    ("$element_id", elementId),
                    ("$names_format_text", (object)element.setters?.GetValue("names-format") ?? DBNull.Value));

                int ordinal = 1;
                foreach (var nameGroup in element.setters?.names ?? Enumerable.Empty<Names>())
                {
                    foreach (var nameValue in nameGroup.names ?? Enumerable.Empty<string>())
                    {
                        ExecuteInsert(connection, transaction,
                            "INSERT INTO race_name_groups (race_element_id, ordinal, name_group_type, name_value) VALUES ($race_element_id, $ordinal, $name_group_type, $name_value);",
                            ("$race_element_id", elementId),
                            ("$ordinal", ordinal++),
                            ("$name_group_type", (object)nameGroup.type ?? DBNull.Value),
                            ("$name_value", nameValue));
                    }
                }
                return;
            }

            if (string.Equals(element.type, "Sub Race", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO subraces (element_id, parent_support_text) VALUES ($element_id, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Race Variant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(element.type, "Dragonmark", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO race_variants (element_id, variant_kind, parent_support_text) VALUES ($element_id, $variant_kind, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$variant_kind", element.type),
                    ("$parent_support_text", (object)GetPreferredSupportText(element.supports, "Race Variant") ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Background", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction, "INSERT INTO backgrounds (element_id) VALUES ($element_id);", ("$element_id", elementId));
                return;
            }

            if (string.Equals(element.type, "Background Variant", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO background_variants (element_id, parent_support_text) VALUES ($element_id, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$parent_support_text", (object)GetPreferredSupportText(element.supports, "Background Variant") ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Feat", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO feats (element_id, allow_duplicate) VALUES ($element_id, $allow_duplicate);",
                    ("$element_id", elementId),
                    ("$allow_duplicate", element.setters?.GetBoolean("allow duplicate") == true ? 1 : 0));
                return;
            }

            if (string.Equals(element.type, "Language", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO languages (element_id, script_text, speakers_text, is_standard, is_exotic, is_secret)
VALUES ($element_id, $script_text, $speakers_text, $is_standard, $is_exotic, $is_secret);",
                    ("$element_id", elementId),
                    ("$script_text", (object)element.setters?.script ?? DBNull.Value),
                    ("$speakers_text", (object)element.setters?.speakers ?? DBNull.Value),
                    ("$is_standard", element.setters?.standard == true ? 1 : 0),
                    ("$is_exotic", element.setters?.exotic == true ? 1 : 0),
                    ("$is_secret", element.setters?.secret == true ? 1 : 0));
                return;
            }

            if (string.Equals(element.type, "Proficiency", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO proficiencies (element_id, proficiency_group, proficiency_subgroup) VALUES ($element_id, $proficiency_group, $proficiency_subgroup);",
                    ("$element_id", elementId),
                    ("$proficiency_group", (object)element.supports?.FirstOrDefault() ?? DBNull.Value),
                    ("$proficiency_subgroup", element.supports?.Count > 1 ? element.supports[1] : DBNull.Value));
                return;
            }

            if (IsFeatureType(element.type))
            {
                var minimumLevel = GetMinimumLevel(element);

                ExecuteInsert(connection, transaction,
                    "INSERT INTO features (element_id, feature_kind, parent_support_text, min_level) VALUES ($element_id, $feature_kind, $parent_support_text, $min_level);",
                    ("$element_id", elementId),
                    ("$feature_kind", element.type),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value),
                    ("$min_level", minimumLevel.HasValue ? minimumLevel.Value : DBNull.Value));
                return;
            }

            if (IsItemType(element.type))
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO items
(element_id, item_kind, cost_text, weight_text, damage_dice_text, damage_type_text, armor_class_text, properties_text, speed_text, capacity_text)
VALUES
($element_id, $item_kind, $cost_text, $weight_text, $damage_dice_text, $damage_type_text, $armor_class_text, $properties_text, $speed_text, $capacity_text);",
                    ("$element_id", elementId),
                    ("$item_kind", element.type),
                    ("$cost_text", (object)element.setters?.GetValue("cost") ?? DBNull.Value),
                    ("$weight_text", (object)element.setters?.GetValue("weight") ?? DBNull.Value),
                    ("$damage_dice_text", (object)element.setters?.GetValue("damage") ?? DBNull.Value),
                    ("$damage_type_text", (object)element.setters?.GetValue("damage type") ?? DBNull.Value),
                    ("$armor_class_text", (object)element.setters?.GetValue("armor class") ?? DBNull.Value),
                    ("$properties_text", (object)element.supports?.raw ?? DBNull.Value),
                    ("$speed_text", (object)element.setters?.GetValue("speed") ?? DBNull.Value),
                    ("$capacity_text", (object)element.setters?.GetValue("capacity") ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Companion", StringComparison.OrdinalIgnoreCase))
            {
                var crText = element.setters?.GetValue("challenge");
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO companions
(element_id, size_text, creature_type, alignment, ac_text, hp_text, speed_text,
 str_score, dex_score, con_score, int_score, wis_score, cha_score,
 skills_text, resistances_text, immunities_text, condition_immunities_text,
 senses_text, languages_text, challenge_text, cr_value, proficiency_bonus, actions_text)
VALUES
($element_id, $size_text, $creature_type, $alignment, $ac_text, $hp_text, $speed_text,
 $str_score, $dex_score, $con_score, $int_score, $wis_score, $cha_score,
 $skills_text, $resistances_text, $immunities_text, $condition_immunities_text,
 $senses_text, $languages_text, $challenge_text, $cr_value, $proficiency_bonus, $actions_text);",
                    ("$element_id",               elementId),
                    ("$size_text",                (object)element.setters?.GetValue("size")               ?? DBNull.Value),
                    ("$creature_type",            (object)element.setters?.GetValue("type")               ?? DBNull.Value),
                    ("$alignment",                (object)element.setters?.GetValue("alignment")          ?? DBNull.Value),
                    ("$ac_text",                  (object)element.setters?.GetValue("ac")                 ?? DBNull.Value),
                    ("$hp_text",                  (object)element.setters?.GetValue("hp")                 ?? DBNull.Value),
                    ("$speed_text",               (object)element.setters?.GetValue("speed")              ?? DBNull.Value),
                    ("$str_score",                ParseIntSetter(element.setters?.GetValue("strength"))),
                    ("$dex_score",                ParseIntSetter(element.setters?.GetValue("dexterity"))),
                    ("$con_score",                ParseIntSetter(element.setters?.GetValue("constitution"))),
                    ("$int_score",                ParseIntSetter(element.setters?.GetValue("intelligence"))),
                    ("$wis_score",                ParseIntSetter(element.setters?.GetValue("wisdom"))),
                    ("$cha_score",                ParseIntSetter(element.setters?.GetValue("charisma"))),
                    ("$skills_text",              (object)element.setters?.GetValue("skills")             ?? DBNull.Value),
                    ("$resistances_text",         (object)element.setters?.GetValue("resistances")        ?? DBNull.Value),
                    ("$immunities_text",          (object)element.setters?.GetValue("immunities")         ?? DBNull.Value),
                    ("$condition_immunities_text",(object)element.setters?.GetValue("condition-immunities") ?? DBNull.Value),
                    ("$senses_text",              (object)element.setters?.GetValue("senses")             ?? DBNull.Value),
                    ("$languages_text",           (object)element.setters?.GetValue("languages")          ?? DBNull.Value),
                    ("$challenge_text",           (object)crText                                          ?? DBNull.Value),
                    ("$cr_value",                 ParseCrValue(crText)),
                    ("$proficiency_bonus",        ParseIntSetter(element.setters?.GetValue("proficiency"))),
                    ("$actions_text",             (object)element.setters?.GetValue("actions")            ?? DBNull.Value));
                return;
            }

            // Companion Action and Companion Trait are simple text elements (description + sheet).
            // The base element row + element_texts are sufficient; no subtype record is needed.
            // They are referenced by the parent Companion's actions_text (comma-separated aurora_ids).
        }

        private static void InsertClassMulticlass(SqliteConnection connection, SqliteTransaction transaction, long elementId, Multiclass multiclass)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO class_multiclass
(class_element_id, multiclass_aurora_id, prerequisite_text, requirements_text, proficiencies_text)
VALUES
($class_element_id, $multiclass_aurora_id, $prerequisite_text, $requirements_text, $proficiencies_text);",
                ("$class_element_id", elementId),
                ("$multiclass_aurora_id", (object)multiclass.id ?? DBNull.Value),
                ("$prerequisite_text", (object)multiclass.prerequisite ?? DBNull.Value),
                ("$requirements_text", (object)multiclass.requirements?.raw ?? DBNull.Value),
                ("$proficiencies_text", (object)multiclass.setters?.GetValue("multiclass proficiencies") ?? DBNull.Value));
        }

        private static void InsertSpellcastingProfile(SqliteConnection connection, SqliteTransaction transaction, long elementId, string elementType, Spellcasting spellcasting)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO spellcasting_profiles
(owner_element_id, owner_kind, profile_name, ability_name, is_extended, prepare_spells, allow_replace, list_text, extend_text)
VALUES
($owner_element_id, $owner_kind, $profile_name, $ability_name, $is_extended, $prepare_spells, $allow_replace, $list_text, $extend_text);",
                ("$owner_element_id", elementId),
                ("$owner_kind", GetSpellcastingOwnerKind(elementType)),
                ("$profile_name", spellcasting.name ?? "Spellcasting"),
                ("$ability_name", (object)spellcasting.ability ?? DBNull.Value),
                ("$is_extended", spellcasting.extend ? 1 : 0),
                ("$prepare_spells", spellcasting.prepare.HasValue ? (spellcasting.prepare.Value ? 1 : 0) : DBNull.Value),
                ("$allow_replace", spellcasting.allowReplace.HasValue ? (spellcasting.allowReplace.Value ? 1 : 0) : DBNull.Value),
                ("$list_text", (object)spellcasting.list?.raw ?? DBNull.Value),
                ("$extend_text", (object)spellcasting.extendList?.raw ?? DBNull.Value));
        }

        private static void InsertSetters(SqliteConnection connection, SqliteTransaction transaction, long elementId, string ownerKind, AuroraSetters setters)
        {
            if (setters?.entries?.Any() != true)
                return;

            long setterScopeId = InsertSetterScope(connection, transaction, ownerKind, elementId);

            int ordinal = 1;
            foreach (var setterEntry in setters.entries)
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO setter_entries
(setter_scope_id, ordinal, setter_name, setter_value)
VALUES
($setter_scope_id, $ordinal, $setter_name, $setter_value);",
                    ("$setter_scope_id", setterScopeId),
                    ("$ordinal", ordinal++),
                    ("$setter_name", setterEntry.name ?? string.Empty),
                    ("$setter_value", (object)setterEntry.value ?? DBNull.Value));

                long setterEntryId = GetLastInsertRowId(connection, transaction);
                int attributeOrdinal = 1;
                foreach (var attribute in setterEntry.attributes)
                {
                    ExecuteInsert(connection, transaction,
                        @"INSERT INTO setter_entry_attributes
(setter_entry_id, ordinal, attribute_name, attribute_value)
VALUES
($setter_entry_id, $ordinal, $attribute_name, $attribute_value);",
                        ("$setter_entry_id", setterEntryId),
                        ("$ordinal", attributeOrdinal++),
                        ("$attribute_name", attribute.Key),
                        ("$attribute_value", (object)attribute.Value ?? DBNull.Value));
                }
            }
        }

        private static void InsertExtract(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraExtract extract)
        {
            if (extract == null)
                return;

            if (string.IsNullOrWhiteSpace(extract.description) && !(extract.items?.Any() == true))
                return;

            ExecuteInsert(connection, transaction,
                @"INSERT INTO element_extracts
(element_id, description_text)
VALUES
($element_id, $description_text);",
                ("$element_id", elementId),
                ("$description_text", (object)extract.description ?? DBNull.Value));

            int ordinal = 1;
            foreach (var item in extract.items ?? Enumerable.Empty<AuroraItemEntry>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO element_extract_items
(element_id, ordinal, item_text, target_aurora_id, amount_text)
VALUES
($element_id, $ordinal, $item_text, $target_aurora_id, $amount_text);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$item_text", (object)item.value ?? DBNull.Value),
                    ("$target_aurora_id", (object)GetItemTargetAuroraId(item) ?? DBNull.Value),
                    ("$amount_text", (object)item.GetAttribute("amount") ?? DBNull.Value));

                long extractItemId = GetLastInsertRowId(connection, transaction);
                int attributeOrdinal = 1;
                foreach (var attribute in item.attributes)
                {
                    ExecuteInsert(connection, transaction,
                        @"INSERT INTO element_extract_item_attributes
(extract_item_id, ordinal, attribute_name, attribute_value)
VALUES
($extract_item_id, $ordinal, $attribute_name, $attribute_value);",
                        ("$extract_item_id", extractItemId),
                        ("$ordinal", attributeOrdinal++),
                        ("$attribute_name", attribute.Key),
                        ("$attribute_value", (object)attribute.Value ?? DBNull.Value));
                }
            }
        }

        private static void InsertRules(SqliteConnection connection, SqliteTransaction transaction, long elementId, string ownerKind, Rules rules)
        {
            if (rules == null)
                return;

            if (!(rules.grants?.Any() == true || rules.selects?.Any() == true || rules.stats?.Any() == true))
                return;

            long ruleScopeId = InsertRuleScope(connection, transaction, ownerKind, elementId);

            int ordinal = 1;
            foreach (var grant in rules.grants ?? Enumerable.Empty<Grant>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO grants
(rule_scope_id, ordinal, grant_type, target_aurora_id, name_text, grant_level, requirements_text)
VALUES
($rule_scope_id, $ordinal, $grant_type, $target_aurora_id, $name_text, $grant_level, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$grant_type", grant.type ?? string.Empty),
                    ("$target_aurora_id", (object)grant.id ?? DBNull.Value),
                    ("$name_text", (object)grant.name ?? DBNull.Value),
                    ("$grant_level", grant.level.HasValue ? grant.level.Value : DBNull.Value),
                    ("$requirements_text", (object)grant.requirements?.raw ?? DBNull.Value));
            }

            ordinal = 1;
            foreach (var select in rules.selects ?? Enumerable.Empty<Select>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO selects
(rule_scope_id, ordinal, select_type, name_text, supports_text, select_level, number_to_choose, default_choice_text, is_optional, spellcasting_profile_id, requirements_text)
VALUES
($rule_scope_id, $ordinal, $select_type, $name_text, $supports_text, $select_level, $number_to_choose, $default_choice_text, $is_optional, $spellcasting_profile_id, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$select_type", select.type ?? string.Empty),
                    ("$name_text", select.name ?? string.Empty),
                    ("$supports_text", (object)select.supports?.raw ?? DBNull.Value),
                    ("$select_level", select.level.HasValue ? select.level.Value : DBNull.Value),
                    ("$number_to_choose", select.number),
                    ("$default_choice_text", (object)select.defaultChoice ?? DBNull.Value),
                    ("$is_optional", select.optional ? 1 : 0),
                    ("$spellcasting_profile_id", ResolveSpellcastingProfileId(connection, transaction, elementId, select.spellcasting)),
                    ("$requirements_text", (object)select.requirements?.raw ?? DBNull.Value));

                long selectId = GetLastInsertRowId(connection, transaction);
                int supportOrdinal = 1;
                foreach (var support in select.supports ?? Enumerable.Empty<string>())
                {
                    ExecuteInsert(connection, transaction,
                        "INSERT INTO select_supports (select_id, ordinal, support_text) VALUES ($select_id, $ordinal, $support_text);",
                        ("$select_id", selectId),
                        ("$ordinal", supportOrdinal++),
                        ("$support_text", support));
                }

                InsertSelectItems(connection, transaction, selectId, select.items);
            }

            ordinal = 1;
            foreach (var stat in rules.stats ?? Enumerable.Empty<Stat>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO stats
(rule_scope_id, ordinal, stat_name, value_expression_text, bonus_expression_text, equipped_expression_text, stat_level, inline_display, alt_text, requirements_text)
VALUES
($rule_scope_id, $ordinal, $stat_name, $value_expression_text, $bonus_expression_text, $equipped_expression_text, $stat_level, $inline_display, $alt_text, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$stat_name", stat.name ?? string.Empty),
                    ("$value_expression_text", (object)stat.value ?? DBNull.Value),
                    ("$bonus_expression_text", (object)stat.bonus ?? DBNull.Value),
                    ("$equipped_expression_text", (object)stat.equipped?.raw ?? DBNull.Value),
                    ("$stat_level", stat.level.HasValue ? stat.level.Value : DBNull.Value),
                    ("$inline_display", stat.inline ? 1 : 0),
                    ("$alt_text", (object)stat.alt ?? DBNull.Value),
                    ("$requirements_text", (object)stat.requirements?.raw ?? DBNull.Value));
            }
        }

        private static void InsertSelectItems(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long selectId,
            IEnumerable<AuroraItemEntry> items)
        {
            if (items?.Any() != true)
                return;

            int ordinal = 1;
            foreach (var item in items)
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO select_items
(select_id, ordinal, item_text, target_aurora_id)
VALUES
($select_id, $ordinal, $item_text, $target_aurora_id);",
                    ("$select_id", selectId),
                    ("$ordinal", ordinal++),
                    ("$item_text", (object)item.value ?? DBNull.Value),
                    ("$target_aurora_id", (object)GetItemTargetAuroraId(item) ?? DBNull.Value));

                long selectItemId = GetLastInsertRowId(connection, transaction);
                int attributeOrdinal = 1;
                foreach (var attribute in item.attributes)
                {
                    ExecuteInsert(connection, transaction,
                        @"INSERT INTO select_item_attributes
(select_item_id, ordinal, attribute_name, attribute_value)
VALUES
($select_item_id, $ordinal, $attribute_name, $attribute_value);",
                        ("$select_item_id", selectItemId),
                        ("$ordinal", attributeOrdinal++),
                        ("$attribute_name", attribute.Key),
                        ("$attribute_value", (object)attribute.Value ?? DBNull.Value));
                }
            }
        }

        private static long InsertRuleScope(SqliteConnection connection, SqliteTransaction transaction, string ownerKind, long ownerElementId)
        {
            ExecuteInsert(connection, transaction,
                "INSERT INTO rule_scopes (owner_kind, owner_element_id, scope_key) VALUES ($owner_kind, $owner_element_id, $scope_key);",
                ("$owner_kind", ownerKind),
                ("$owner_element_id", ownerElementId),
                ("$scope_key", ownerKind == "class-multiclass" ? "multiclass" : "element"));
            return GetLastInsertRowId(connection, transaction);
        }

        private static long InsertSetterScope(SqliteConnection connection, SqliteTransaction transaction, string ownerKind, long ownerElementId)
        {
            ExecuteInsert(connection, transaction,
                "INSERT INTO setter_scopes (owner_kind, owner_element_id, scope_key) VALUES ($owner_kind, $owner_element_id, $scope_key);",
                ("$owner_kind", ownerKind),
                ("$owner_element_id", ownerElementId),
                ("$scope_key", ownerKind == "class-multiclass" ? "multiclass" : "element"));
            return GetLastInsertRowId(connection, transaction);
        }

        private static object ResolveSpellcastingProfileId(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long ownerElementId,
            string spellcastingProfileName)
        {
            if (string.IsNullOrWhiteSpace(spellcastingProfileName))
                return DBNull.Value;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT spellcasting_profile_id
FROM spellcasting_profiles
WHERE owner_element_id = $owner_element_id
  AND profile_name = $profile_name
LIMIT 1;";
            command.Parameters.AddWithValue("$owner_element_id", ownerElementId);
            command.Parameters.AddWithValue("$profile_name", spellcastingProfileName);
            return command.ExecuteScalar() ?? DBNull.Value;
        }

        private static string GetItemTargetAuroraId(AuroraItemEntry item)
        {
            string attributeId = item?.GetAttribute("id");
            if (!string.IsNullOrWhiteSpace(attributeId)
                && attributeId.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
            {
                return attributeId;
            }

            if (!string.IsNullOrWhiteSpace(item?.value)
                && item.value.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
            {
                return item.value;
            }

            return null;
        }

        private static void InsertSpellRecord(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraSpell spell)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO spells
(element_id, spell_level, school_name, casting_time_text, range_text, duration_text, has_verbal, has_somatic, has_material, material_text, is_concentration, is_ritual, attack_type, damage_type_text, damage_formula_text, dc_ability_name, dc_success_text, source_url)
VALUES
($element_id, $spell_level, $school_name, $casting_time_text, $range_text, $duration_text, $has_verbal, $has_somatic, $has_material, $material_text, $is_concentration, $is_ritual, $attack_type, $damage_type_text, $damage_formula_text, $dc_ability_name, $dc_success_text, $source_url);",
                ("$element_id", elementId),
                ("$spell_level", spell.level),
                ("$school_name", (object)spell.school?.index ?? DBNull.Value),
                ("$casting_time_text", (object)spell.casting_time ?? DBNull.Value),
                ("$range_text", (object)spell.range ?? DBNull.Value),
                ("$duration_text", (object)spell.duration ?? DBNull.Value),
                ("$has_verbal", spell.hasVerbal ? 1 : 0),
                ("$has_somatic", spell.hasSomatic ? 1 : 0),
                ("$has_material", spell.hasMaterial ? 1 : 0),
                ("$material_text", (object)spell.material ?? DBNull.Value),
                ("$is_concentration", spell.concentration ? 1 : 0),
                ("$is_ritual", spell.ritual ? 1 : 0),
                ("$attack_type", (object)spell.attack_type ?? DBNull.Value),
                ("$damage_type_text", (object)spell.damage?.damage_type?.index ?? DBNull.Value),
                ("$damage_formula_text", JsonSerializer.Serialize(spell.damage?.damage_at_slot_level, new JsonSerializerOptions { IncludeFields = true })),
                ("$dc_ability_name", (object)spell.dc?.index ?? DBNull.Value),
                ("$dc_success_text", (object)spell.dc?.dc_success ?? DBNull.Value),
                ("$source_url", (object)spell.url ?? DBNull.Value));

            if (spell.classes?.Any() == true)
            {
                int ordinal = 1;
                foreach (var access in spell.classes)
                {
                    ExecuteInsert(connection, transaction,
                        "INSERT INTO spell_access (spell_element_id, ordinal, access_text) VALUES ($spell_element_id, $ordinal, $access_text);",
                        ("$spell_element_id", elementId),
                        ("$ordinal", ordinal++),
                    ("$access_text", access.name));
                }
            }
        }

        private static void RebuildExpressionCatalog(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, "DELETE FROM expression_usages;");
            ExecuteSql(connection, transaction, "DELETE FROM expression_nodes;");
            ExecuteSql(connection, transaction, "DELETE FROM expressions;");

            var cache = new Dictionary<string, long>(StringComparer.Ordinal);
            var sources = new[]
            {
                new ExpressionSource("element-requirement", "requirement_text",
                    "SELECT element_id, ordinal, requirement_text FROM element_requirements WHERE trim(requirement_text) <> '';"),
                new ExpressionSource("element-support", "support_text",
                    "SELECT element_id, ordinal, support_text FROM element_supports WHERE trim(support_text) <> '';"),
                new ExpressionSource("select-support", "support_text",
                    "SELECT select_id, ordinal, support_text FROM select_supports WHERE trim(support_text) <> '';"),
                new ExpressionSource("grant", "requirements_text",
                    "SELECT grant_id, 1 AS ordinal, requirements_text FROM grants WHERE requirements_text IS NOT NULL AND trim(requirements_text) <> '';"),
                new ExpressionSource("select", "requirements_text",
                    "SELECT select_id, 1 AS ordinal, requirements_text FROM selects WHERE requirements_text IS NOT NULL AND trim(requirements_text) <> '';"),
                new ExpressionSource("stat", "requirements_text",
                    "SELECT stat_id, 1 AS ordinal, requirements_text FROM stats WHERE requirements_text IS NOT NULL AND trim(requirements_text) <> '';"),
                new ExpressionSource("stat", "equipped_expression_text",
                    "SELECT stat_id, 1 AS ordinal, equipped_expression_text FROM stats WHERE equipped_expression_text IS NOT NULL AND trim(equipped_expression_text) <> '';"),
                new ExpressionSource("class-multiclass", "requirements_text",
                    "SELECT class_element_id, 1 AS ordinal, requirements_text FROM class_multiclass WHERE requirements_text IS NOT NULL AND trim(requirements_text) <> '';"),
                new ExpressionSource("spellcasting-profile", "list_text",
                    "SELECT spellcasting_profile_id, 1 AS ordinal, list_text FROM spellcasting_profiles WHERE list_text IS NOT NULL AND trim(list_text) <> '';"),
                new ExpressionSource("spellcasting-profile", "extend_text",
                    "SELECT spellcasting_profile_id, 1 AS ordinal, extend_text FROM spellcasting_profiles WHERE extend_text IS NOT NULL AND trim(extend_text) <> '';")
            };

            foreach (var source in sources)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = source.Sql;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long ownerId = reader.GetInt64(0);
                    int ordinal = reader.GetInt32(1);
                    string rawText = reader.IsDBNull(2) ? null : reader.GetString(2)?.Trim();
                    if (string.IsNullOrWhiteSpace(rawText))
                        continue;

                    long expressionId = EnsureExpression(connection, transaction, cache, rawText);
                    ExecuteInsert(connection, transaction,
                        @"INSERT INTO expression_usages
(expression_id, owner_kind, owner_id, field_name, ordinal, source_text)
VALUES
($expression_id, $owner_kind, $owner_id, $field_name, $ordinal, $source_text);",
                        ("$expression_id", expressionId),
                        ("$owner_kind", source.OwnerKind),
                        ("$owner_id", ownerId),
                        ("$field_name", source.FieldName),
                        ("$ordinal", ordinal),
                        ("$source_text", rawText));
                }
            }
        }

        private static long EnsureExpression(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Dictionary<string, long> cache,
            string rawText)
        {
            if (cache.TryGetValue(rawText, out long existingExpressionId))
                return existingExpressionId;

            AuroraExpressionParseResult parseResult = AuroraExpressionEngine.Parse(rawText);

            ExecuteInsert(connection, transaction,
                @"INSERT INTO expressions
(raw_text, normalized_text, parse_status, error_text)
VALUES
($raw_text, $normalized_text, $parse_status, $error_text);",
                ("$raw_text", rawText),
                ("$normalized_text", NormalizeExpressionText(rawText)),
                ("$parse_status", parseResult.Status),
                ("$error_text", (object)parseResult.ErrorText ?? DBNull.Value));

            long expressionId = GetLastInsertRowId(connection, transaction);
            InsertExpressionNode(connection, transaction, expressionId, null, 1, parseResult.RootNode);
            cache[rawText] = expressionId;
            return expressionId;
        }

        private static void InsertExpressionNode(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long expressionId,
            long? parentNodeId,
            int ordinal,
            AuroraExpressionNode expressionNode)
        {
            if (expressionNode == null)
                return;

            ExecuteInsert(connection, transaction,
                @"INSERT INTO expression_nodes
(expression_id, parent_node_id, ordinal, node_kind, value_type, value_text)
VALUES
($expression_id, $parent_node_id, $ordinal, $node_kind, $value_type, $value_text);",
                ("$expression_id", expressionId),
                ("$parent_node_id", parentNodeId.HasValue ? parentNodeId.Value : DBNull.Value),
                ("$ordinal", ordinal),
                ("$node_kind", expressionNode.Kind),
                ("$value_type", (object)expressionNode.ValueType ?? DBNull.Value),
                ("$value_text", (object)expressionNode.ValueText ?? DBNull.Value));

            long expressionNodeId = GetLastInsertRowId(connection, transaction);
            int childOrdinal = 1;
            foreach (var childNode in expressionNode.Children)
            {
                InsertExpressionNode(connection, transaction, expressionId, expressionNodeId, childOrdinal++, childNode);
            }
        }

        private static string NormalizeExpressionText(string rawText)
        {
            return string.IsNullOrWhiteSpace(rawText)
                ? string.Empty
                : rawText.Trim().ToLowerInvariant();
        }

        private static void ResolveDeferredRelationships(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
UPDATE grants
SET target_element_id =
(
    SELECT MIN(e.element_id)
    FROM elements AS e
    WHERE e.aurora_id = grants.target_aurora_id
)
WHERE target_element_id IS NULL
  AND target_aurora_id IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE element_extract_items
SET linked_element_id =
(
    SELECT MIN(e.element_id)
    FROM elements AS e
    WHERE e.aurora_id = element_extract_items.target_aurora_id
       OR (element_extract_items.target_aurora_id IS NULL AND e.name = element_extract_items.item_text)
)
WHERE linked_element_id IS NULL
  AND (target_aurora_id IS NOT NULL OR item_text IS NOT NULL);");

            ExecuteSql(connection, transaction, @"
UPDATE select_items
SET linked_element_id =
(
    SELECT MIN(e.element_id)
    FROM elements AS e
    WHERE e.aurora_id = select_items.target_aurora_id
       OR (select_items.target_aurora_id IS NULL AND e.name = select_items.item_text)
)
WHERE linked_element_id IS NULL
  AND (target_aurora_id IS NOT NULL OR item_text IS NOT NULL);");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    WHERE parent.aurora_id = subraces.parent_support_text
       OR parent.name = subraces.parent_support_text
       OR subraces.parent_support_text = parent.name || ' Subrace'
       OR subraces.parent_support_text = parent.name || ' Ancestry'
       OR subraces.parent_support_text LIKE '% ' || parent.name
)
WHERE race_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE race_variants
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    WHERE parent.aurora_id = race_variants.parent_support_text
       OR parent.name = race_variants.parent_support_text
       OR race_variants.parent_support_text = parent.name || ' Variant'
       OR trim(replace(replace(race_variants.parent_support_text, 'Variant ', ''), ' Variant', '')) = parent.name
)
WHERE race_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE background_variants
SET background_element_id =
(
    SELECT MIN(parent.element_id)
    FROM backgrounds AS b
    JOIN elements AS parent ON parent.element_id = b.element_id
    WHERE parent.aurora_id = background_variants.parent_support_text
       OR parent.name = background_variants.parent_support_text
       OR background_variants.parent_support_text = 'Variant ' || parent.name
       OR trim(replace(background_variants.parent_support_text, 'Variant ', '')) = parent.name
)
WHERE background_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT MIN(parent.element_id)
    FROM elements AS parent
    WHERE parent.aurora_id = features.parent_support_text
       OR parent.name = features.parent_support_text
)
WHERE parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS class_element
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    WHERE et.type_name = 'Class'
      AND
      (
          class_element.name = archetypes.parent_support_text
          OR archetypes.parent_support_text = class_element.name || ' Subclass'
          OR (archetypes.parent_support_text = 'Sacred Oath' AND class_element.name = 'Paladin')
          OR (archetypes.parent_support_text = 'Divine Domain' AND class_element.name = 'Cleric')
          OR (archetypes.parent_support_text = 'Bard College' AND class_element.name = 'Bard')
          OR (archetypes.parent_support_text = 'Druid Circle' AND class_element.name = 'Druid')
          OR (archetypes.parent_support_text = 'Martial Archetype' AND class_element.name = 'Fighter')
          OR (archetypes.parent_support_text = 'Monastic Tradition' AND class_element.name = 'Monk')
          OR (archetypes.parent_support_text = 'Ranger Archetype' AND class_element.name = 'Ranger')
          OR (archetypes.parent_support_text = 'Ranger Conclave' AND class_element.name = 'Ranger')
          OR (archetypes.parent_support_text = 'Roguish Archetype' AND class_element.name = 'Rogue')
          OR (archetypes.parent_support_text = 'Sorcerous Origin' AND class_element.name = 'Sorcerer')
          OR (archetypes.parent_support_text = 'Arcane Tradition' AND class_element.name = 'Wizard')
          OR (archetypes.parent_support_text = 'Otherworldly Patron' AND class_element.name = 'Warlock')
          OR (archetypes.parent_support_text = 'Primal Path' AND class_element.name = 'Barbarian')
      )
)
WHERE parent_class_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS archetype_element
    JOIN elements AS class_element ON class_element.source_file_id = archetype_element.source_file_id
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    WHERE archetype_element.element_id = archetypes.element_id
      AND et.type_name = 'Class'
)
WHERE parent_class_element_id IS NULL;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO support_tags (support_text, normalized_text)
SELECT support_text, lower(trim(support_text))
FROM
(
    SELECT support_text FROM element_supports
    UNION
    SELECT support_text FROM select_supports
);");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO support_tags (support_text, normalized_text, support_kind)
VALUES ('[[inline-item]]', '[[inline-item]]', 'bounded-option-set');");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO element_support_links
(
    element_id,
    ordinal,
    support_tag_id,
    linked_element_id,
    resolution_kind,
    is_primary_parent
)
SELECT
    es.element_id,
    es.ordinal,
    st.support_tag_id,
    COALESCE(
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.aurora_id = es.support_text),
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.name = es.support_text)
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.aurora_id = es.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.name = es.support_text) THEN 'element-name'
        WHEN es.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind,
    0 AS is_primary_parent
FROM element_supports AS es
JOIN support_tags AS st
    ON st.support_text = es.support_text;");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT a.parent_class_element_id
        FROM archetypes AS a
        WHERE a.element_id = element_support_links.element_id
    ),
    resolution_kind = 'archetype-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM archetypes AS a
      WHERE a.element_id = element_support_links.element_id
        AND a.parent_class_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT s.race_element_id
        FROM subraces AS s
        WHERE s.element_id = element_support_links.element_id
    ),
    resolution_kind = 'subrace-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM subraces AS s
      WHERE s.element_id = element_support_links.element_id
        AND s.race_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT f.parent_element_id
        FROM features AS f
        WHERE f.element_id = element_support_links.element_id
    ),
    resolution_kind = 'feature-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM features AS f
      WHERE f.element_id = element_support_links.element_id
        AND f.parent_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_support_links
(
    select_id,
    ordinal,
    support_tag_id,
    linked_element_id,
    resolution_kind
)
SELECT
    ss.select_id,
    ss.ordinal,
    st.support_tag_id,
    COALESCE(
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.aurora_id = ss.support_text),
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.name = ss.support_text)
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.aurora_id = ss.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.name = ss.support_text) THEN 'element-name'
        WHEN ss.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind
FROM select_supports AS ss
JOIN support_tags AS st
    ON st.support_text = ss.support_text;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    es.element_id,
    ssl.support_tag_id,
    'support-membership'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN element_supports AS esupport
    ON esupport.support_text = st.support_text
JOIN elements AS es
    ON es.element_id = esupport.element_id;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    e.element_id,
    ssl.support_tag_id,
    'direct-id'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN elements AS e
    ON e.aurora_id = st.support_text;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    e.element_id,
    ssl.support_tag_id,
    'direct-name'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN elements AS e
    ON e.name = st.support_text;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    si.select_id,
    si.linked_element_id,
    (SELECT support_tag_id FROM support_tags WHERE support_text = '[[inline-item]]'),
    CASE
        WHEN si.target_aurora_id IS NOT NULL THEN 'inline-item-id'
        ELSE 'inline-item-text'
    END
FROM select_items AS si
WHERE si.linked_element_id IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'dynamic-expression'
WHERE support_text LIKE '$(%';");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'dynamic-expression'
WHERE support_kind = 'unclassified'
  AND
  (
      support_text LIKE '%||%'
      OR support_text LIKE '%&&%'
      OR support_text LIKE '!(%'
      OR support_text LIKE '!%'
      OR support_text LIKE '%,%'
      OR support_text LIKE '(%'
      OR support_text GLOB '[0-9]'
      OR support_text GLOB '[0-9][0-9]'
      OR support_text GLOB 'ID_*|*'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'direct-parent'
WHERE EXISTS
(
    SELECT 1
    FROM element_support_links AS esl
    WHERE esl.support_tag_id = support_tags.support_tag_id
      AND esl.is_primary_parent = 1
);");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'broad-option-set'
WHERE support_kind = 'unclassified'
  AND normalized_text IN
  (
      'skill',
      'tool',
      'language',
      'weapon',
      'armor',
      'item',
      'magic item',
      'mount',
      'vehicle',
      'ammunition',
      'general',
      'melee',
      'ranged',
      'simple',
      'martial',
      'musical instrument',
      'artisan''s tools',
      'artisan tools',
      'gaming set',
      'vehicle (land)',
      'vehicle (water)',
      'class',
      'race',
      'spell attack'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND normalized_text IN
  (
      'abjuration',
      'conjuration',
      'divination',
      'enchantment',
      'evocation',
      'illusion',
      'necromancy',
      'transmutation',
      'ritual',
      'companion',
      'familiar',
      'background variant',
      'custom race language',
      'psionic disciplines',
      'sub-feature'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND
  (
      normalized_text = 'starting'
      OR normalized_text LIKE '% discipline'
      OR normalized_text LIKE '% specialization'
      OR normalized_text LIKE 'variant %'
      OR normalized_text LIKE '% variant'
      OR normalized_text LIKE '% companion'
      OR normalized_text LIKE '% companions'
      OR normalized_text LIKE '% spirit'
      OR normalized_text LIKE 'spirit bonded %'
      OR normalized_text LIKE 'undead servant %'
      OR normalized_text LIKE 'ua artificer %'
      OR normalized_text LIKE 'ua2020% %'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM select_option_links AS sol
      WHERE sol.support_tag_id = support_tags.support_tag_id
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM element_support_links AS esl
      WHERE esl.support_tag_id = support_tags.support_tag_id
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM select_support_links AS ssl
      WHERE ssl.support_tag_id = support_tags.support_tag_id
  );");
        }

        private sealed record ExpressionSource(string OwnerKind, string FieldName, string Sql);

        private static void ExecuteSql(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static long GetLastInsertRowId(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT last_insert_rowid();";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        private static void ExecuteInsert(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        private static string GetSpellcastingOwnerKind(string elementType)
        {
            if (string.Equals(elementType, "Class", StringComparison.OrdinalIgnoreCase))
                return "class";

            if (string.Equals(elementType, "Archetype", StringComparison.OrdinalIgnoreCase))
                return "archetype";

            return "feature";
        }

        private static int DetermineLoaderPriority(string elementType)
        {
            return elementType?.ToLowerInvariant() switch
            {
                "source" => 5,
                "race" => 10,
                "sub race" => 20,
                "race variant" => 25,
                "dragonmark" => 26,
                "class" => 30,
                "archetype" => 40,
                "background" => 50,
                "background variant" => 55,
                "feat" => 60,
                "language" => 70,
                "proficiency" => 80,
                "spell" => 90,
                "class feature" => 100,
                "archetype feature" => 110,
                "racial trait" => 120,
                "background feature" => 130,
                "background sub-feature" => 135,
                "feat feature" => 140,
                "ability score improvement" => 150,
                "grants" => 160,
                "companion" => 200,
                "companion action" => 210,
                "companion reaction" => 215,
                "companion trait" => 210,
                "companion feature" => 210,
                "monster" => 220,
                "weapon property" => 230,
                "weapon group" => 235,
                "option" => 300,
                "support" => 310,
                "rule" => 320,
                "information" => 330,
                "deity" => 340,
                "alignment" => 350,
                "vision" => 360,
                "condition" => 370,
                "magic school" => 380,
                "background characteristics" => 390,
                _ => 500
            };
        }

        /// <summary>
        /// Parses a setter string as an integer parameter value, returning DBNull if parsing fails.
        /// </summary>
        private static object ParseIntSetter(string value) =>
            int.TryParse(value?.Trim(), out var n) ? (object)n : DBNull.Value;

        /// <summary>
        /// Converts a CR string ("0", "1/8", "1/4", "1/2", "1" … "30") to a REAL value
        /// suitable for numeric comparison, returning DBNull if the string is unrecognized.
        /// </summary>
        private static object ParseCrValue(string crText)
        {
            if (string.IsNullOrWhiteSpace(crText)) return DBNull.Value;
            return crText.Trim() switch
            {
                "0"   => (object)0.0,
                "1/8" => 0.125,
                "1/4" => 0.25,
                "1/2" => 0.5,
                _     => double.TryParse(crText.Trim(), out var d) ? (object)d : DBNull.Value
            };
        }

        private static int? GetMinimumLevel(AuroraElement element)
        {
            List<int> levels = new();

            if (element.sheet?.description?.Any() == true)
            {
                levels.AddRange(element.sheet.description.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.grants?.Any() == true)
            {
                levels.AddRange(element.rules.grants.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.selects?.Any() == true)
            {
                levels.AddRange(element.rules.selects.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.stats?.Any() == true)
            {
                levels.AddRange(element.rules.stats.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            return levels.Count > 0 ? levels.Min() : null;
        }

        private static bool IsFeatureType(string elementType)
        {
            return string.Equals(elementType, "Class Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Archetype Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Racial Trait", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Background Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Feat Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Ability Score Improvement", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPreferredSupportText(AuroraTextCollection supports, params string[] ignoredSupports)
        {
            if (supports == null || supports.Count == 0)
                return null;

            HashSet<string> ignored = new(
                ignoredSupports?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            string preferred = supports.FirstOrDefault(x => !ignored.Contains(x));
            return string.IsNullOrWhiteSpace(preferred)
                ? supports.FirstOrDefault()
                : preferred;
        }

        private static bool IsItemType(string elementType)
        {
            return string.Equals(elementType, "Item", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Weapon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Armor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Ammunition", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Mount", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Vehicle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Magic Item", StringComparison.OrdinalIgnoreCase);
        }

        // ── SRD creature import ──────────────────────────────────────────────────

        /// <summary>
        /// Imports SRD monsters from the 5e-bits/5e-database JSON file into the
        /// <c>creatures</c> table of an existing Aurora SQLite database, then
        /// name-matches them against Aurora Companion elements to populate
        /// <c>creature_aurora_links</c>.
        /// Always performs a full replace of the creatures table (DELETE + re-insert)
        /// because creature rows have no natural key to upsert against.
        /// </summary>
        public static void ImportSrdCreatures(string jsonPath, string sqlitePath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"SRD monsters JSON not found: {jsonPath}");
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found — run sqlite-import first: {sqlitePath}");

            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString());
            connection.Open();
            ExecuteSql(connection, null, "PRAGMA foreign_keys = ON;");
            using var transaction = connection.BeginTransaction();

            int inserted = ImportSrdCreaturesIfChanged(connection, transaction, jsonPath, force: true);

            transaction.Commit();
            Console.WriteLine($"Imported {inserted} SRD creatures from {Path.GetFileName(jsonPath)} into {sqlitePath}.");
        }

        /// <summary>
        /// Imports SRD creatures within an existing transaction only if the JSON
        /// file hash has changed since last import. Returns the number of creatures
        /// inserted, or 0 if skipped. Pass <paramref name="force"/> = true to
        /// skip the hash check (used by the standalone <c>srd-creatures</c> command).
        /// </summary>
        private static int ImportSrdCreaturesIfChanged(
            SqliteConnection connection, SqliteTransaction transaction,
            string jsonPath, bool force = false)
        {
            string hash = ComputeFileHash(jsonPath);

            if (!force)
            {
                // Check stored hash in import_state.
                using var check = connection.CreateCommand();
                check.Transaction = transaction;
                check.CommandText =
                    "SELECT file_hash FROM import_state WHERE key = 'srd-monsters';";
                string storedHash = check.ExecuteScalar() as string;
                if (storedHash == hash) return 0;
            }

            // Full replace of SRD creatures (no upsert key available).
            ExecuteSql(connection, transaction,
                "DELETE FROM creatures WHERE source_kind = 'srd';");

            var monsters = JsonSerializer.Deserialize<List<SrdMonster>>(
                File.ReadAllText(jsonPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (monsters == null || monsters.Count == 0) return 0;

            foreach (var m in monsters)
                InsertSrdCreature(connection, transaction, m);

            LinkCreaturesToAuroraCompanions(connection, transaction);

            // Persist the hash.
            ExecuteInsert(connection, transaction,
                "INSERT OR REPLACE INTO import_state (key, file_hash, imported_utc) VALUES ($key, $hash, $utc);",
                ("$key", (object)"srd-monsters"),
                ("$hash", (object)hash),
                ("$utc", (object)DateTime.UtcNow.ToString("o")));

            return monsters.Count;
        }

        private static void InsertSrdCreature(SqliteConnection connection, SqliteTransaction transaction, _5eApiTranslator.Models.SrdMonster m)
        {
            var crText           = SrdHelpers.FormatCr(m.ChallengeRating);
            var acText           = SrdHelpers.FormatAc(m.ArmorClass);
            var speedText        = SrdHelpers.FormatSpeed(m.Speed);
            var savingThrowsText = SrdHelpers.FormatSavingThrows(m.Proficiencies);
            var skillsText       = SrdHelpers.FormatSkills(m.Proficiencies);
            var sensesText       = SrdHelpers.FormatSenses(m.Senses);
            var conditionImmunitiesText = m.ConditionImmunities?.Count > 0
                ? string.Join(", ", m.ConditionImmunities.Select(ci => ci.Name))
                : null;

            ExecuteInsert(connection, transaction,
                @"INSERT INTO creatures
(name, slug, cr_text, cr_value, size_text, creature_type, subtype_text, alignment,
 ac_text, hp_average, hp_text, speed_text,
 str_score, dex_score, con_score, int_score, wis_score, cha_score,
 saving_throws_text, skills_text,
 damage_vulnerabilities_text, damage_resistances_text, damage_immunities_text,
 condition_immunities_text, senses_text, languages_text,
 proficiency_bonus, source_kind, source_name)
VALUES
($name, $slug, $cr_text, $cr_value, $size_text, $creature_type, $subtype_text, $alignment,
 $ac_text, $hp_average, $hp_text, $speed_text,
 $str_score, $dex_score, $con_score, $int_score, $wis_score, $cha_score,
 $saving_throws_text, $skills_text,
 $damage_vulnerabilities_text, $damage_resistances_text, $damage_immunities_text,
 $condition_immunities_text, $senses_text, $languages_text,
 $proficiency_bonus, $source_kind, $source_name);",
                ("$name",                        (object)m.Name),
                ("$slug",                        (object)(m.Index ?? m.Name?.Trim().ToLower().Replace(" ", "-"))),
                ("$cr_text",                     crText),
                ("$cr_value",                    (object)m.ChallengeRating),
                ("$size_text",                   (object)m.Size ?? DBNull.Value),
                ("$creature_type",               (object)m.Type ?? DBNull.Value),
                ("$subtype_text",                (object)(string.IsNullOrWhiteSpace(m.Subtype) ? null : m.Subtype) ?? DBNull.Value),
                ("$alignment",                   (object)m.Alignment ?? DBNull.Value),
                ("$ac_text",                     (object)acText ?? DBNull.Value),
                ("$hp_average",                  (object)m.HitPoints),
                ("$hp_text",                     (object)m.HitPointsRoll ?? DBNull.Value),
                ("$speed_text",                  (object)speedText ?? DBNull.Value),
                ("$str_score",                   (object)m.Strength),
                ("$dex_score",                   (object)m.Dexterity),
                ("$con_score",                   (object)m.Constitution),
                ("$int_score",                   (object)m.Intelligence),
                ("$wis_score",                   (object)m.Wisdom),
                ("$cha_score",                   (object)m.Charisma),
                ("$saving_throws_text",          (object)savingThrowsText ?? DBNull.Value),
                ("$skills_text",                 (object)skillsText ?? DBNull.Value),
                ("$damage_vulnerabilities_text", m.DamageVulnerabilities?.Count > 0 ? string.Join(", ", m.DamageVulnerabilities) : (object)DBNull.Value),
                ("$damage_resistances_text",     m.DamageResistances?.Count > 0 ? string.Join(", ", m.DamageResistances) : (object)DBNull.Value),
                ("$damage_immunities_text",      m.DamageImmunities?.Count > 0 ? string.Join(", ", m.DamageImmunities) : (object)DBNull.Value),
                ("$condition_immunities_text",   (object)conditionImmunitiesText ?? DBNull.Value),
                ("$senses_text",                 (object)sensesText ?? DBNull.Value),
                ("$languages_text",              (object)(string.IsNullOrWhiteSpace(m.Languages) ? null : m.Languages) ?? DBNull.Value),
                ("$proficiency_bonus",           (object)m.ProficiencyBonus),
                ("$source_kind",                 "srd"),
                ("$source_name",                 "SRD 5.1"));
        }

        /// <summary>
        /// Name-matches SRD creatures against Aurora Companion elements and
        /// inserts rows into <c>creature_aurora_links</c>. Safe to run multiple
        /// times — uses INSERT OR IGNORE on the primary key.
        /// </summary>
        internal static void LinkCreaturesToAuroraCompanions(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO creature_aurora_links (creature_id, element_id, link_kind)
SELECT
    c.creature_id,
    e.element_id,
    'name-match'
FROM creatures AS c
JOIN elements AS e
    ON lower(trim(e.name)) = lower(trim(c.name))
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
WHERE et.type_name = 'Companion';");
        }

    }
}
