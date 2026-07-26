using AuroraTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuroraTranslator
{
    internal static class AuroraSqliteImporter
    {
        private const int CurrentDataVersion = 10;

        private static readonly IReadOnlyDictionary<string, (string TargetName, IReadOnlyList<string> TypeNames)> GrantTargetAliasMap
            = new Dictionary<string, (string TargetName, IReadOnlyList<string> TypeNames)>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID_LANGUAGE_Draconic"] = ("Draconic", new[] { "Language" }),
                ["ID_LANGUAGE_Infernal"] = ("Infernal", new[] { "Language" }),
                ["ID_GFP_PHB_SPELL_POISON_SPRAY"] = ("Poison Spray", new[] { "Spell" }),
                ["ID_PHB_SPELL_ANIMATE_OBJECT"] = ("Animate Objects", new[] { "Spell" }),
                ["ID_PHB_SPELL_BANISH"] = ("Banishment", new[] { "Spell" }),
                ["ID_PHB_SPELL_CAUSE_FEAR"] = ("Cause Fear", new[] { "Spell" }),
                ["ID_PHB_SPELL_ERUPTING_EARTH"] = ("Erupting Earth", new[] { "Spell" }),
                ["ID_PHB_SPELL_SUMMON_GREATER_DEMONS"] = ("Summon Greater Demon", new[] { "Spell" }),
                ["ID_PHB_SPELL_SUMMON_LESSER_DEMONS"] = ("Summon Lesser Demons", new[] { "Spell" }),
                ["ID_PHB_SPELL_TELEPATHIC_BOND"] = ("Rary’s Telepathic Bond", new[] { "Spell" }),
                ["ID_PHB_SPELL_WALL_OF_FLAME"] = ("Wall of Fire", new[] { "Spell" }),
                ["ID_RGTTYR_FEATURE_REPLACEMENT_BENDER_EXTRA_ATTACK"] = ("Improved Extra Attack: Bender", new[] { "Class Feature" }),
                ["ID_RGTTYR_FEAT_FOCUSED_DISCIPLINE_FEATURES"] = ("Focused Discipline", new[] { "Feat" })
            };

        private static readonly IReadOnlyDictionary<string, (string TargetName, IReadOnlyList<string> TypeNames)> ExtractTargetAliasMap
            = new Dictionary<string, (string TargetName, IReadOnlyList<string> TypeNames)>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID_WOTC_PHB24_WEAPON_CROSSBOW_LIGHT"] = ("Light Crossbow", new[] { "Weapon" })
            };

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
            var existingFiles = LoadExistingSourceFiles(connection, transaction);
            var sourceFileIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var changedPaths  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPaths     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in catalog.Files)
            {
                seenPaths.Add(file.RelativePath);
                string hash = ComputeFileHash(file.FullPath);
                long contentPackageId = EnsureContentPackage(connection, transaction, file);

                if (existingFiles.TryGetValue(file.RelativePath, out var existing))
                {
                    if (existing.Hash == hash)
                    {
                        // Unchanged — reuse existing ID, skip element re-import.
                        UpdateSourceFileMetadata(connection, transaction, existing.Id, file, contentPackageId, hash);
                        sourceFileIds[file.RelativePath] = existing.Id;
                        continue;
                    }
                    // Changed — delete cascade, then re-import below.
                    DeleteSourceFile(connection, transaction, existing.Id);
                }

                long newId = InsertSourceFile(connection, transaction, file, contentPackageId, hash);
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

            // Re-resolve precedence-sensitive relationships every run so package
            // changes take effect even when XML file contents are unchanged.
            RefreshPrecedenceResolution(connection, transaction);

            // Rebuild the expression catalog only when imported Aurora content changed.
            if (changedPaths.Count > 0)
                RebuildExpressionCatalog(connection, transaction);

            transaction.Commit();

            WriteImportMetadata(connection, catalog.Files.Count);

            int skipped = catalog.Files.Count - changedPaths.Count;
            Console.WriteLine(
                $"Aurora import: {addedElements} elements processed " +
                $"({changedPaths.Count} files changed, {skipped} unchanged).");
            if (srdAdded > 0)
                Console.WriteLine($"SRD creatures: {srdAdded} creatures imported/updated.");
            else if (!string.IsNullOrEmpty(srdJsonPath))
                Console.WriteLine("SRD creatures: no changes.");
        }

        // schema_version and data_version must match AuroraDatabaseVersions in the
        // Aurora-Lights repo (Aurora.Importer/AuroraDatabaseMetadata.cs).
        private static void WriteImportMetadata(SqliteConnection connection, int sourceFileCount)
        {
            long elementCount;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM elements;";
                elementCount = (long)(countCmd.ExecuteScalar() ?? 0L);
            }

            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO database_metadata
    (singleton_id, schema_version, data_version, importer_version,
     built_utc, source_file_count, element_count, content_root_hash)
VALUES
    (1, 1, $data_version, $importer_version, $built_utc, $source_file_count, $element_count, NULL)
ON CONFLICT(singleton_id) DO UPDATE SET
    schema_version    = excluded.schema_version,
    data_version      = excluded.data_version,
    importer_version  = excluded.importer_version,
    built_utc         = excluded.built_utc,
    source_file_count = excluded.source_file_count,
    element_count     = excluded.element_count;";
            cmd.Parameters.AddWithValue("$data_version", CurrentDataVersion);
            cmd.Parameters.AddWithValue("$importer_version", "AuroraTranslator/1.0");
            cmd.Parameters.AddWithValue("$built_utc", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$source_file_count", sourceFileCount);
            cmd.Parameters.AddWithValue("$element_count", elementCount);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public static List<ContentPackageInfo> ListContentPackages(string sqlitePath, string schemaPath = null)
        {
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection);
            EnsureResolutionCachePopulated(connection);
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    cp.package_key,
    cp.package_name,
    cp.package_kind,
    cp.precedence_rank,
    cp.is_enabled,
    COALESCE(file_counts.file_count, 0) AS file_count,
    COALESCE(winner_counts.winning_element_count, 0) AS winning_element_count,
    COALESCE(duplicate_counts.duplicate_element_count, 0) AS duplicate_element_count
FROM content_packages AS cp
LEFT JOIN
(
    SELECT
        content_package_id,
        COUNT(*) AS file_count
    FROM source_files
    GROUP BY content_package_id
) AS file_counts
    ON file_counts.content_package_id = cp.content_package_id
LEFT JOIN
(
    SELECT
        content_package_id,
        COUNT(*) AS winning_element_count
    FROM resolved_elements_cache
    GROUP BY content_package_id
) AS winner_counts
    ON winner_counts.content_package_id = cp.content_package_id
LEFT JOIN
(
    SELECT
        sf.content_package_id,
        COUNT(*) AS duplicate_element_count
    FROM elements AS e
    JOIN source_files AS sf
        ON sf.source_file_id = e.source_file_id
    JOIN
    (
        SELECT aurora_id
        FROM elements
        WHERE aurora_id IS NOT NULL
          AND trim(aurora_id) <> ''
        GROUP BY aurora_id
        HAVING COUNT(*) > 1
    ) AS dup_ids
        ON dup_ids.aurora_id = e.aurora_id
    GROUP BY sf.content_package_id
) AS duplicate_counts
    ON duplicate_counts.content_package_id = cp.content_package_id
ORDER BY
    cp.is_enabled DESC,
    cp.precedence_rank DESC,
    cp.package_name ASC;";

            var packages = new List<ContentPackageInfo>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                packages.Add(new ContentPackageInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4) != 0,
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7)));
            }

            return packages;
        }

        public static void UpdateContentPackageSettings(
            string sqlitePath,
            string packageKey,
            int? precedenceRank = null,
            bool? isEnabled = null,
            string schemaPath = null)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
                throw new ArgumentException("Package key is required.", nameof(packageKey));
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");
            if (!precedenceRank.HasValue && !isEnabled.HasValue)
                throw new ArgumentException("At least one package setting must be supplied.");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection);
            using var transaction = connection.BeginTransaction();
            UpdateContentPackageSettingsCore(connection, transaction, packageKey, precedenceRank, isEnabled, useScopedRefresh: true);
            transaction.Commit();
        }

        public static PackageRefreshParityResult ValidatePackageRefreshParity(
            string sqlitePath,
            string packageKey,
            int? precedenceRank = null,
            bool? isEnabled = null)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
                throw new ArgumentException("Package key is required.", nameof(packageKey));
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");
            if (!precedenceRank.HasValue && !isEnabled.HasValue)
                throw new ArgumentException("At least one package setting must be supplied.");

            string tempRoot = Path.Combine(Path.GetTempPath(), "AuroraTranslatorParity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string scopedPath = Path.Combine(tempRoot, "scoped.sqlite");
            string fullPath = Path.Combine(tempRoot, "full.sqlite");
            File.Copy(sqlitePath, scopedPath, overwrite: true);
            File.Copy(sqlitePath, fullPath, overwrite: true);

            try
            {
                using (var scopedConnection = OpenSqliteConnection(scopedPath))
                {
                    EnsurePackageAdministrationSchema(scopedConnection);
                    using var transaction = scopedConnection.BeginTransaction();
                    UpdateContentPackageSettingsCore(scopedConnection, transaction, packageKey, precedenceRank, isEnabled, useScopedRefresh: true);
                    transaction.Commit();
                }

                using (var fullConnection = OpenSqliteConnection(fullPath))
                {
                    EnsurePackageAdministrationSchema(fullConnection);
                    using var transaction = fullConnection.BeginTransaction();
                    UpdateContentPackageSettingsCore(fullConnection, transaction, packageKey, precedenceRank, isEnabled, useScopedRefresh: false);
                    transaction.Commit();
                }

                var tableResults = CompareParityDatabases(scopedPath, fullPath);
                return new PackageRefreshParityResult(packageKey, precedenceRank, isEnabled, tableResults);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup only. Leaving temp copies behind is acceptable.
                }
            }
        }

        public static void RefreshPackageResolution(string sqlitePath, string schemaPath = null)
        {
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection);
            using var transaction = connection.BeginTransaction();
            RefreshPrecedenceResolution(connection, transaction);
            transaction.Commit();
        }

        public static void RefreshPackageAdministrationViews(string sqlitePath)
        {
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection, refreshViews: true);
        }

        public static UnresolvedLinkDiagnosticsReport GetUnresolvedLinkDiagnostics(
            string sqlitePath,
            int topPatternsPerKind = 10,
            int sampleOwnersPerPattern = 3)
        {
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");
            if (topPatternsPerKind <= 0)
                throw new ArgumentOutOfRangeException(nameof(topPatternsPerKind), "Top pattern count must be greater than zero.");
            if (sampleOwnersPerPattern <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleOwnersPerPattern), "Sample owner count must be greater than zero.");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection, refreshViews: true);

            long totalUnresolvedCount;
            long actionableUnresolvedCount;
            using (var totalCommand = connection.CreateCommand())
            {
                totalCommand.CommandText = "SELECT COUNT(*) FROM v_unresolved_loader_link_diagnostics;";
                totalUnresolvedCount = (long)(totalCommand.ExecuteScalar() ?? 0L);
            }

            using (var actionableCommand = connection.CreateCommand())
            {
                actionableCommand.CommandText = @"
SELECT COUNT(*)
FROM v_unresolved_loader_link_diagnostics
WHERE diagnostic_status = 'actionable';";
                actionableUnresolvedCount = (long)(actionableCommand.ExecuteScalar() ?? 0L);
            }

            var deferredSummaries = new List<UnresolvedLinkDeferredSummary>();
            using (var deferredCommand = connection.CreateCommand())
            {
                deferredCommand.CommandText = @"
SELECT
    diagnostic_status,
    diagnostic_reason,
    link_kind,
    COUNT(*) AS total_count
FROM v_unresolved_loader_link_diagnostics
WHERE diagnostic_status <> 'actionable'
GROUP BY diagnostic_status, diagnostic_reason, link_kind
ORDER BY total_count DESC, diagnostic_status ASC, link_kind ASC;";

                using var deferredReader = deferredCommand.ExecuteReader();
                while (deferredReader.Read())
                {
                    string diagnosticStatus = deferredReader.GetString(0);
                    string diagnosticReason = deferredReader.IsDBNull(1) ? null : deferredReader.GetString(1);
                    string linkKind = deferredReader.GetString(2);
                    int totalCount = Convert.ToInt32(deferredReader.GetInt64(3));
                    deferredSummaries.Add(new UnresolvedLinkDeferredSummary(diagnosticStatus, diagnosticReason, linkKind, totalCount));
                }
            }

            var kindSummaries = new List<UnresolvedLinkKindSummary>();

            using (var kindCommand = connection.CreateCommand())
            {
                kindCommand.CommandText = @"
SELECT
    link_kind,
    COUNT(*) AS total_count
FROM v_unresolved_loader_link_diagnostics
WHERE diagnostic_status = 'actionable'
GROUP BY link_kind
ORDER BY total_count DESC, link_kind ASC;";

                using var kindReader = kindCommand.ExecuteReader();
                while (kindReader.Read())
                {
                    string linkKind = kindReader.GetString(0);
                    int totalCount = Convert.ToInt32(kindReader.GetInt64(1));
                    var patterns = LoadUnresolvedPatterns(connection, linkKind, topPatternsPerKind, sampleOwnersPerPattern);
                    kindSummaries.Add(new UnresolvedLinkKindSummary(linkKind, totalCount, patterns));
                }
            }

            return new UnresolvedLinkDiagnosticsReport(totalUnresolvedCount, actionableUnresolvedCount, deferredSummaries, kindSummaries);
        }

        public static SourceIntegrityDiagnosticsReport GetSourceIntegrityDiagnostics(
            string sqlitePath,
            int topPatternsPerKind = 10,
            int sampleRowsPerPattern = 3)
        {
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");
            if (topPatternsPerKind <= 0)
                throw new ArgumentOutOfRangeException(nameof(topPatternsPerKind), "Top pattern count must be greater than zero.");
            if (sampleRowsPerPattern <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRowsPerPattern), "Sample row count must be greater than zero.");

            using var connection = OpenSqliteConnection(sqlitePath);
            EnsurePackageAdministrationSchema(connection, refreshViews: true);

            int totalIssueCount;
            using (var totalCommand = connection.CreateCommand())
            {
                totalCommand.CommandText = "SELECT COUNT(*) FROM v_source_integrity_issues;";
                totalIssueCount = Convert.ToInt32((long)(totalCommand.ExecuteScalar() ?? 0L));
            }

            var kindSummaries = new List<SourceIntegrityKindSummary>();
            using (var kindCommand = connection.CreateCommand())
            {
                kindCommand.CommandText = @"
SELECT
    issue_kind,
    COUNT(*) AS total_count
FROM v_source_integrity_issues
GROUP BY issue_kind
ORDER BY total_count DESC, issue_kind ASC;";

                using var kindReader = kindCommand.ExecuteReader();
                while (kindReader.Read())
                {
                    string issueKind = kindReader.GetString(0);
                    int totalCount = Convert.ToInt32(kindReader.GetInt64(1));
                    var patterns = LoadSourceIntegrityPatterns(connection, issueKind, topPatternsPerKind, sampleRowsPerPattern);
                    kindSummaries.Add(new SourceIntegrityKindSummary(issueKind, totalCount, patterns));
                }
            }

            return new SourceIntegrityDiagnosticsReport(totalIssueCount, kindSummaries);
        }

        // ── Schema / DB setup ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the schema SQL to the database. All DDL uses <c>IF NOT EXISTS</c> /
        /// <c>INSERT OR IGNORE</c> guards, making this safe to re-run on an existing DB.
        /// Running it on every open also picks up new tables added to the schema after
        /// a database was first created. Then runs <see cref="ApplyMigrations"/> for any
        /// changes (ADD COLUMN) that cannot be expressed with IF NOT EXISTS.
        /// </summary>
        private static void UpdateContentPackageSettingsCore(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string packageKey,
            int? precedenceRank,
            bool? isEnabled,
            bool useScopedRefresh)
        {
            using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT COUNT(*) FROM content_packages WHERE package_key = $package_key;";
            exists.Parameters.AddWithValue("$package_key", packageKey);
            if ((long)exists.ExecuteScalar() == 0)
                throw new InvalidOperationException($"No content package was found with key '{packageKey}'.");

            var assignments = new List<string>();
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.Parameters.AddWithValue("$package_key", packageKey);

            if (precedenceRank.HasValue)
            {
                assignments.Add("precedence_rank = $precedence_rank");
                update.Parameters.AddWithValue("$precedence_rank", precedenceRank.Value);
            }

            if (isEnabled.HasValue)
            {
                assignments.Add("is_enabled = $is_enabled");
                update.Parameters.AddWithValue("$is_enabled", isEnabled.Value ? 1 : 0);
            }

            update.CommandText = $@"
UPDATE content_packages
SET {string.Join(", ", assignments)}
WHERE package_key = $package_key;";
            update.ExecuteNonQuery();

            if (useScopedRefresh)
                RefreshPrecedenceResolutionForPackage(connection, transaction, packageKey);
            else
                RefreshPrecedenceResolution(connection, transaction);
        }

        private static void BackfillSelectItemOptionKinds(SqliteConnection connection)
        {
            using var classify = connection.CreateCommand();
            classify.CommandText = @"
UPDATE select_items
SET option_kind = 'aurora-reference'
WHERE target_aurora_id IS NOT NULL
  AND trim(target_aurora_id) <> '';

UPDATE select_items
SET option_kind = 'text-choice'
WHERE (target_aurora_id IS NULL OR trim(target_aurora_id) = '')
  AND
  (
      EXISTS
      (
          SELECT 1
          FROM selects AS s
          WHERE s.select_id = select_items.select_id
            AND lower(trim(s.select_type)) = 'list'
      )
      OR
      item_text IS NULL
      OR trim(item_text) = ''
      OR item_text LIKE '%.%'
      OR item_text LIKE '%,%'
      OR item_text LIKE '%;%'
      OR item_text LIKE '%:%'
      OR length(trim(item_text)) >= 60
      OR (
          length(trim(item_text)) - length(replace(trim(item_text), ' ', '')) + 1
      ) >= 8
      OR EXISTS
      (
          SELECT 1
          FROM selects AS s
          WHERE s.select_id = select_items.select_id
            AND
            (
                lower(s.name_text) LIKE '%personality%'
                OR lower(s.name_text) LIKE '%ideal%'
                OR lower(s.name_text) LIKE '%bond%'
                OR lower(s.name_text) LIKE '%flaw%'
                OR lower(s.name_text) LIKE '%specialty%'
                OR lower(s.name_text) LIKE '%speciality%'
                OR lower(s.name_text) LIKE '%trait%'
                OR lower(s.name_text) LIKE '%harrowing event%'
                OR lower(s.name_text) LIKE '%memento%'
                OR lower(s.name_text) LIKE '%life event%'
                OR lower(s.name_text) LIKE '%favorite scheme%'
                OR lower(s.name_text) LIKE '%guild business%'
                OR lower(s.name_text) LIKE '%characteristic%'
            )
      )
  );

UPDATE select_items
SET option_kind = 'name-reference-candidate'
WHERE option_kind IS NULL
   OR trim(option_kind) = ''
   OR option_kind NOT IN ('aurora-reference', 'name-reference-candidate', 'text-choice');";
            classify.ExecuteNonQuery();
        }

        private static void SeedParentFamilyAliases(SqliteConnection connection)
        {
            using var seed = connection.CreateCommand();
            seed.CommandText = @"
INSERT OR IGNORE INTO parent_family_aliases
(alias_text, link_kind, target_name, target_type_name, target_aurora_id, resolution_kind, priority)
VALUES
('Replicate Magic Item Option', 'feature-parent', 'Replicate Magic Item', 'Class Feature', NULL, 'target-name', 100),
('Artificer Infusion', 'feature-parent', 'Infuse Item', 'Class Feature', NULL, 'target-name', 100),
('UA Artificer Infusion', 'feature-parent', 'Infuse Item', 'Class Feature', NULL, 'target-name', 100),
('Kibbles Psionic Talent', 'feature-parent', 'Psionic Talents', 'Class Feature', NULL, 'target-name', 100),
('Kensei Weapon', 'feature-parent', 'Path of the Kensei', 'Archetype Feature', NULL, 'target-name', 100),
('Humanoid Favored Enemy', 'feature-parent', 'Favored Enemy', 'Class Feature', NULL, 'target-name', 100),
('PHB24 Eldritch Invocation', 'feature-parent', 'Level 1: Eldritch Invocations', 'Class Feature', NULL, 'target-name', 100),
('Weapon Mastery', 'feature-parent', 'Level 1: Weapon Mastery', 'Class Feature', NULL, 'target-name', 100),
('Improvement Option', 'feature-parent', 'Ability Score Improvement', 'Class Feature', NULL, 'target-name', 100),
('Blood Hunter Order', 'archetype-parent', 'Blood Hunter', 'Class', NULL, 'target-name', 100),
('Artificer Specialist', 'archetype-parent', 'Artificer', 'Class', NULL, 'target-name', 100),
('UA Artificer Specialist', 'archetype-parent', 'Artificer', 'Class', NULL, 'target-name', 100),
('Bender Discipline', 'archetype-parent', 'Bender', 'Class', NULL, 'target-name', 100),
('Avenger Archetype', 'archetype-parent', 'Avenger', 'Class', NULL, 'target-name', 100),
('Kibbles Psionic Archetype', 'archetype-parent', 'Psion', 'Class', NULL, 'target-name', 100);";
            seed.ExecuteNonQuery();
        }

        private static IReadOnlyList<UnresolvedLinkPatternSummary> LoadUnresolvedPatterns(
            SqliteConnection connection,
            string linkKind,
            int topPatternsPerKind,
            int sampleOwnersPerPattern)
        {
            var patterns = new List<UnresolvedLinkPatternSummary>();

            using var patternCommand = connection.CreateCommand();
            patternCommand.CommandText = @"
WITH ranked AS
(
    SELECT
        link_kind,
        unresolved_key,
        unresolved_text,
        COUNT(*) AS unresolved_count,
        ROW_NUMBER() OVER (
            PARTITION BY link_kind
            ORDER BY COUNT(*) DESC,
                     COALESCE(unresolved_key, unresolved_text, '') ASC,
                     COALESCE(unresolved_text, unresolved_key, '') ASC
        ) AS rank_in_kind
    FROM v_unresolved_loader_link_diagnostics
    WHERE link_kind = $link_kind
      AND diagnostic_status = 'actionable'
    GROUP BY link_kind, unresolved_key, unresolved_text
)
SELECT
    unresolved_key,
    unresolved_text,
    unresolved_count
FROM ranked
WHERE rank_in_kind <= $top_patterns
ORDER BY unresolved_count DESC,
         COALESCE(unresolved_key, unresolved_text, '') ASC,
         COALESCE(unresolved_text, unresolved_key, '') ASC;";
            patternCommand.Parameters.AddWithValue("$link_kind", linkKind);
            patternCommand.Parameters.AddWithValue("$top_patterns", topPatternsPerKind);

            using var patternReader = patternCommand.ExecuteReader();
            while (patternReader.Read())
            {
                string unresolvedKey = patternReader.IsDBNull(0) ? null : patternReader.GetString(0);
                string unresolvedText = patternReader.IsDBNull(1) ? null : patternReader.GetString(1);
                int count = Convert.ToInt32(patternReader.GetInt64(2));
                var sampleOwners = LoadSampleOwners(connection, linkKind, unresolvedKey, unresolvedText, sampleOwnersPerPattern);
                string displayText = NormalizeDiagnosticValue(string.IsNullOrWhiteSpace(unresolvedText) ? unresolvedKey : unresolvedText);
                string displayKey = string.IsNullOrWhiteSpace(unresolvedKey)
                    ? displayText
                    : NormalizeDiagnosticValue(unresolvedKey);

                patterns.Add(new UnresolvedLinkPatternSummary(
                    unresolvedKey,
                    unresolvedText,
                    displayKey,
                    displayText,
                    count,
                    sampleOwners));
            }

            return patterns;
        }

        private static IReadOnlyList<string> LoadSampleOwners(
            SqliteConnection connection,
            string linkKind,
            string unresolvedKey,
            string unresolvedText,
            int sampleOwnersPerPattern)
        {
            var owners = new List<string>();

            using var ownerCommand = connection.CreateCommand();
            ownerCommand.CommandText = @"
SELECT DISTINCT
    owner_name,
    owner_type_name,
    owner_aurora_id
FROM v_unresolved_loader_link_diagnostics
WHERE link_kind = $link_kind
  AND diagnostic_status = 'actionable'
  AND ((unresolved_key = $unresolved_key) OR (unresolved_key IS NULL AND $unresolved_key IS NULL))
  AND ((unresolved_text = $unresolved_text) OR (unresolved_text IS NULL AND $unresolved_text IS NULL))
ORDER BY owner_name ASC, owner_aurora_id ASC
LIMIT $sample_count;";
            ownerCommand.Parameters.AddWithValue("$link_kind", linkKind);
            ownerCommand.Parameters.AddWithValue("$unresolved_key", (object)unresolvedKey ?? DBNull.Value);
            ownerCommand.Parameters.AddWithValue("$unresolved_text", (object)unresolvedText ?? DBNull.Value);
            ownerCommand.Parameters.AddWithValue("$sample_count", sampleOwnersPerPattern);

            using var ownerReader = ownerCommand.ExecuteReader();
            while (ownerReader.Read())
            {
                string ownerName = ownerReader.IsDBNull(0) ? "(unnamed element)" : ownerReader.GetString(0);
                string ownerType = ownerReader.IsDBNull(1) ? "Unknown" : ownerReader.GetString(1);
                string ownerAuroraId = ownerReader.IsDBNull(2) ? null : ownerReader.GetString(2);
                owners.Add(string.IsNullOrWhiteSpace(ownerAuroraId)
                    ? $"{ownerName} [{ownerType}]"
                    : $"{ownerName} [{ownerType}] ({ownerAuroraId})");
            }

            return owners;
        }

        private static string NormalizeDiagnosticValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();
        }

        private static List<SourceIntegrityPatternSummary> LoadSourceIntegrityPatterns(
            SqliteConnection connection,
            string issueKind,
            int topPatternsPerKind,
            int sampleRowsPerPattern)
        {
            var patterns = new List<SourceIntegrityPatternSummary>();

            using var patternCommand = connection.CreateCommand();
            patternCommand.CommandText = @"
WITH ranked AS
(
    SELECT
        issue_kind,
        issue_key,
        issue_text,
        COUNT(*) AS issue_count,
        ROW_NUMBER() OVER (
            PARTITION BY issue_kind
            ORDER BY COUNT(*) DESC,
                     COALESCE(issue_key, issue_text, '') ASC,
                     COALESCE(issue_text, issue_key, '') ASC
        ) AS rank_in_kind
    FROM v_source_integrity_issues
    WHERE issue_kind = $issue_kind
    GROUP BY issue_kind, issue_key, issue_text
)
SELECT
    issue_key,
    issue_text,
    issue_count
FROM ranked
WHERE rank_in_kind <= $top_patterns
ORDER BY issue_count DESC,
         COALESCE(issue_key, issue_text, '') ASC,
         COALESCE(issue_text, issue_key, '') ASC;";
            patternCommand.Parameters.AddWithValue("$issue_kind", issueKind);
            patternCommand.Parameters.AddWithValue("$top_patterns", topPatternsPerKind);

            using var patternReader = patternCommand.ExecuteReader();
            while (patternReader.Read())
            {
                string issueKey = patternReader.IsDBNull(0) ? null : patternReader.GetString(0);
                string issueText = patternReader.IsDBNull(1) ? null : patternReader.GetString(1);
                int count = Convert.ToInt32(patternReader.GetInt64(2));
                var sampleRows = LoadSourceIntegritySampleRows(connection, issueKind, issueKey, issueText, sampleRowsPerPattern);
                string displayText = NormalizeDiagnosticValue(string.IsNullOrWhiteSpace(issueText) ? issueKey : issueText);
                string displayKey = string.IsNullOrWhiteSpace(issueKey)
                    ? displayText
                    : NormalizeDiagnosticValue(issueKey);

                patterns.Add(new SourceIntegrityPatternSummary(
                    issueKey,
                    issueText,
                    displayKey,
                    displayText,
                    count,
                    sampleRows));
            }

            return patterns;
        }

        private static IReadOnlyList<string> LoadSourceIntegritySampleRows(
            SqliteConnection connection,
            string issueKind,
            string issueKey,
            string issueText,
            int sampleRowsPerPattern)
        {
            var rows = new List<string>();

            using var sampleCommand = connection.CreateCommand();
            sampleCommand.CommandText = @"
SELECT DISTINCT
    relative_path,
    owner_name,
    owner_type_name,
    owner_aurora_id
FROM v_source_integrity_issues
WHERE issue_kind = $issue_kind
  AND ((issue_key = $issue_key) OR (issue_key IS NULL AND $issue_key IS NULL))
  AND ((issue_text = $issue_text) OR (issue_text IS NULL AND $issue_text IS NULL))
ORDER BY relative_path ASC, owner_name ASC, owner_aurora_id ASC
LIMIT $sample_count;";
            sampleCommand.Parameters.AddWithValue("$issue_kind", issueKind);
            sampleCommand.Parameters.AddWithValue("$issue_key", (object)issueKey ?? DBNull.Value);
            sampleCommand.Parameters.AddWithValue("$issue_text", (object)issueText ?? DBNull.Value);
            sampleCommand.Parameters.AddWithValue("$sample_count", sampleRowsPerPattern);

            using var sampleReader = sampleCommand.ExecuteReader();
            while (sampleReader.Read())
            {
                string relativePath = sampleReader.IsDBNull(0) ? "(unknown file)" : sampleReader.GetString(0);
                string ownerName = sampleReader.IsDBNull(1) ? null : sampleReader.GetString(1);
                string ownerType = sampleReader.IsDBNull(2) ? null : sampleReader.GetString(2);
                string ownerAuroraId = sampleReader.IsDBNull(3) ? null : sampleReader.GetString(3);

                rows.Add(string.IsNullOrWhiteSpace(ownerName)
                    ? relativePath
                    : $"{relativePath} :: {ownerName} [{ownerType}] ({ownerAuroraId})");
            }

            return rows;
        }

        private static List<PackageRefreshParityTableResult> CompareParityDatabases(
            string scopedPath,
            string fullPath)
        {
            string[] tablesToCompare =
            {
                "content_packages",
                "resolved_elements_cache",
                "resolved_unique_element_names_cache",
                "support_tags",
                "grants",
                "element_extract_items",
                "select_items",
                "subraces",
                "race_variants",
                "background_variants",
                "features",
                "archetypes",
                "element_support_links",
                "select_support_links",
                "select_option_links"
            };

            using var scopedConnection = OpenSqliteConnection(scopedPath);
            using var attach = scopedConnection.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $baseline AS baseline;";
            attach.Parameters.AddWithValue("$baseline", fullPath);
            attach.ExecuteNonQuery();

            var results = new List<PackageRefreshParityTableResult>();
            foreach (string tableName in tablesToCompare)
            {
                string selectList = BuildComparisonColumnList(scopedConnection, tableName);
                long scopedRowCount = ExecuteLongScalar(scopedConnection, $"SELECT COUNT(*) FROM main.{QuoteIdentifier(tableName)};");
                long fullRowCount = ExecuteLongScalar(scopedConnection, $"SELECT COUNT(*) FROM baseline.{QuoteIdentifier(tableName)};");
                long scopedOnlyCount = ExecuteLongScalar(
                    scopedConnection,
                    $@"SELECT COUNT(*) FROM
(
    SELECT {selectList} FROM main.{QuoteIdentifier(tableName)}
    EXCEPT
    SELECT {selectList} FROM baseline.{QuoteIdentifier(tableName)}
);");
                long fullOnlyCount = ExecuteLongScalar(
                    scopedConnection,
                    $@"SELECT COUNT(*) FROM
(
    SELECT {selectList} FROM baseline.{QuoteIdentifier(tableName)}
    EXCEPT
    SELECT {selectList} FROM main.{QuoteIdentifier(tableName)}
);");

                results.Add(new PackageRefreshParityTableResult(
                    tableName,
                    scopedRowCount,
                    fullRowCount,
                    scopedOnlyCount,
                    fullOnlyCount));
            }

            using var detach = scopedConnection.CreateCommand();
            detach.CommandText = "DETACH DATABASE baseline;";
            detach.ExecuteNonQuery();

            return results;
        }

        private static string BuildComparisonColumnList(SqliteConnection connection, string tableName)
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"SELECT name FROM pragma_table_info({SqliteLiteral(tableName)}) ORDER BY cid;";
            using var reader = pragma.ExecuteReader();

            var columns = new List<string>();
            while (reader.Read())
                columns.Add(QuoteIdentifier(reader.GetString(0)));

            if (columns.Count == 0)
                throw new InvalidOperationException($"Could not determine columns for table '{tableName}'.");

            return string.Join(", ", columns);
        }

        private static long ExecuteLongScalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static string QuoteIdentifier(string identifier)
            => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        private static string SqliteLiteral(string value)
            => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        private static void EnsureSchema(SqliteConnection connection, string schemaPath)
        {
            // Always run the schema SQL — all DDL uses IF NOT EXISTS / INSERT OR IGNORE guards,
            // making it safe to re-run against an existing database. This also handles the case
            // where new tables were added to the schema after the DB was initially created.
            //
            // The .sql file contains PRAGMA / BEGIN TRANSACTION / COMMIT for use with standalone
            // SQLite tools.  Strip those lines before executing programmatically: we manage our
            // own transaction and deliberately leave FK enforcement OFF during bulk import.
            bool grantColumnsBootstrapped = ApplySchemaBootstrapMigrations(connection);

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

            if (grantColumnsBootstrapped)
                InvalidateSourceFileHashes(connection);
        }

        private static SqliteConnection OpenSqliteConnection(string sqlitePath)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString());
            connection.Open();
            ExecuteSql(connection, null, "PRAGMA foreign_keys = ON;");
            return connection;
        }

        private static void EnsurePackageAdministrationSchema(SqliteConnection connection, bool refreshViews = true)
        {
            ApplyMigrations(connection, refreshViews);
        }

        /// <summary>
        /// Applies incremental schema maintenance for package/precedence management.
        /// This includes column additions that cannot be expressed with <c>IF NOT EXISTS</c>,
        /// plus lightweight admin tables, indexes, and cache-backed views that we want
        /// available even when we are not re-running the full schema script.
        /// </summary>
        private static void ApplyMigrations(SqliteConnection connection, bool refreshViews = true)
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

            // M002: add content_package_id to source_files for package precedence support.
            using var packageColumnCheck = connection.CreateCommand();
            packageColumnCheck.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('source_files') WHERE name = 'content_package_id';";
            if ((long)packageColumnCheck.ExecuteScalar() == 0)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE source_files ADD COLUMN content_package_id INTEGER REFERENCES content_packages(content_package_id);";
                alter.ExecuteNonQuery();
            }

            // M003: add option_kind to select_items so text-only select choices
            // can be distinguished from unresolved element references.
            using var selectItemKindCheck = connection.CreateCommand();
            selectItemKindCheck.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('select_items') WHERE name = 'option_kind';";
            if ((long)selectItemKindCheck.ExecuteScalar() == 0)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE select_items ADD COLUMN option_kind TEXT NOT NULL DEFAULT 'name-reference-candidate';";
                alter.ExecuteNonQuery();
            }

            // M004: add semantic grant target columns so Aurora internal/system grants
            // can resolve without requiring synthetic element rows.
            EnsureColumnExists(connection, "grants", "target_semantic_key", "TEXT");
            EnsureColumnExists(connection, "grants", "target_semantic_kind", "TEXT");
            EnsureColumnExists(connection, "grants", "target_semantic_name", "TEXT");
            bool addedGrantSpellcastingName = EnsureColumnExists(connection, "grants", "spellcasting_name", "TEXT");
            bool addedGrantIsPrepared = EnsureColumnExists(connection, "grants", "is_prepared", "INTEGER CHECK (is_prepared IN (0, 1))");
            EnsureColumnExists(connection, "grants", "raw_xml", "TEXT");
            EnsureColumnExists(connection, "selects", "raw_xml", "TEXT");
            EnsureColumnExists(connection, "stats", "raw_xml", "TEXT");

            if (addedGrantSpellcastingName || addedGrantIsPrepared)
                InvalidateSourceFileHashes(connection);

            using var backfillLegacyGrantTargets = connection.CreateCommand();
            backfillLegacyGrantTargets.CommandText = @"
UPDATE grants
SET target_aurora_id = trim(name_text)
WHERE COALESCE(trim(target_aurora_id), '') = ''
  AND COALESCE(trim(name_text), '') <> ''
  AND upper(trim(name_text)) LIKE 'ID\_%' ESCAPE '\';";
            backfillLegacyGrantTargets.ExecuteNonQuery();

            using var cacheTables = connection.CreateCommand();
            cacheTables.CommandText = @"
CREATE TABLE IF NOT EXISTS content_packages
(
    content_package_id INTEGER PRIMARY KEY,
    package_key TEXT NOT NULL UNIQUE,
    package_name TEXT NOT NULL,
    package_kind TEXT NOT NULL DEFAULT 'local' CHECK
    (
        package_kind IN
        (
            'core',
            'official',
            'third-party',
            'homebrew',
            'local'
        )
    ),
    precedence_rank INTEGER NOT NULL DEFAULT 500,
    is_enabled INTEGER NOT NULL DEFAULT 1 CHECK (is_enabled IN (0, 1)),
    package_description TEXT,
    source_url TEXT,
    created_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_content_packages_precedence
    ON content_packages(is_enabled, precedence_rank DESC, package_kind, package_name);
CREATE INDEX IF NOT EXISTS ix_source_files_package
    ON source_files(content_package_id, relative_path);
CREATE INDEX IF NOT EXISTS ix_grants_semantic
    ON grants(target_semantic_key, target_semantic_kind);

CREATE TABLE IF NOT EXISTS resolved_elements_cache
(
    aurora_id TEXT NOT NULL PRIMARY KEY,
    winning_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    source_file_id INTEGER REFERENCES source_files(source_file_id) ON DELETE CASCADE,
    content_package_id INTEGER REFERENCES content_packages(content_package_id),
    package_key TEXT,
    package_name TEXT,
    package_kind TEXT,
    precedence_rank INTEGER,
    duplicate_count INTEGER NOT NULL,
    resolution_rank INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_resolved_elements_cache_element
    ON resolved_elements_cache(winning_element_id, content_package_id);
CREATE INDEX IF NOT EXISTS ix_resolved_elements_cache_package
    ON resolved_elements_cache(content_package_id, aurora_id);

CREATE TABLE IF NOT EXISTS resolved_unique_element_names_cache
(
    normalized_name TEXT NOT NULL PRIMARY KEY,
    winning_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    name TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_resolved_unique_names_element
    ON resolved_unique_element_names_cache(winning_element_id);

CREATE TABLE IF NOT EXISTS parent_family_aliases
(
    alias_text TEXT NOT NULL,
    link_kind TEXT NOT NULL CHECK (link_kind IN ('feature-parent', 'archetype-parent')),
    target_name TEXT,
    target_type_name TEXT,
    target_aurora_id TEXT,
    resolution_kind TEXT NOT NULL DEFAULT 'target-name',
    priority INTEGER NOT NULL DEFAULT 100,
    PRIMARY KEY (alias_text, link_kind)
);
CREATE INDEX IF NOT EXISTS ix_parent_family_aliases_target_name
    ON parent_family_aliases(link_kind, target_name, target_type_name);";
            cacheTables.ExecuteNonQuery();

            EnsureSpellcastingProfileEntriesSchema(connection);

            using var selectItemKindIndex = connection.CreateCommand();
            selectItemKindIndex.CommandText = @"
CREATE INDEX IF NOT EXISTS ix_select_items_kind
    ON select_items(option_kind, linked_element_id);";
            selectItemKindIndex.ExecuteNonQuery();

            using var rebindBackfilledGrantTargets = connection.CreateCommand();
            rebindBackfilledGrantTargets.CommandText = @"
UPDATE grants
SET target_element_id =
(
    SELECT rec.winning_element_id
    FROM resolved_elements_cache AS rec
    WHERE rec.aurora_id = grants.target_aurora_id
)
WHERE target_element_id IS NULL
  AND COALESCE(trim(target_aurora_id), '') <> ''
  AND COALESCE(trim(target_semantic_key), '') = '';";
            rebindBackfilledGrantTargets.ExecuteNonQuery();

            BackfillSelectItemOptionKinds(connection);
            SeedParentFamilyAliases(connection);

            bool rebuildSpellcastingProfileEntries =
                GetStoredDataVersion(connection) < CurrentDataVersion ||
                SpellcastingProfileEntriesNeedRebuild(connection);

            if (rebuildSpellcastingProfileEntries)
                RebuildSpellcastingProfileEntries(connection);

            EnsureDatabaseMetadataDataVersion(connection, CurrentDataVersion);

            if (refreshViews)
            {
                RefreshResolutionViews(connection);
                RefreshAppContractViews(connection);
            }
        }

        private static bool ApplySchemaBootstrapMigrations(SqliteConnection connection)
        {
            EnsureElementTextsSchemaUpToDate(connection);
            EnsureColumnExistsIfTableExists(connection, "source_files", "file_hash", "TEXT");
            EnsureColumnExistsIfTableExists(connection, "source_files", "content_package_id", "INTEGER REFERENCES content_packages(content_package_id)");
            EnsureColumnExistsIfTableExists(connection, "select_items", "option_kind", "TEXT NOT NULL DEFAULT 'name-reference-candidate'");
            EnsureColumnExistsIfTableExists(connection, "grants", "target_semantic_key", "TEXT");
            EnsureColumnExistsIfTableExists(connection, "grants", "target_semantic_kind", "TEXT");
            EnsureColumnExistsIfTableExists(connection, "grants", "target_semantic_name", "TEXT");
            bool addedGrantSpellcastingName = EnsureColumnExistsIfTableExists(connection, "grants", "spellcasting_name", "TEXT");
            bool addedGrantIsPrepared = EnsureColumnExistsIfTableExists(connection, "grants", "is_prepared", "INTEGER CHECK (is_prepared IN (0, 1))");
            EnsureColumnExistsIfTableExists(connection, "grants", "raw_xml", "TEXT");
            EnsureColumnExistsIfTableExists(connection, "selects", "raw_xml", "TEXT");
            EnsureColumnExistsIfTableExists(connection, "stats", "raw_xml", "TEXT");
            return addedGrantSpellcastingName || addedGrantIsPrepared;
        }

        private static void EnsureElementTextsSchemaUpToDate(SqliteConnection connection)
        {
            if (!TableExists(connection, "element_texts"))
                return;

            using var check = connection.CreateCommand();
            check.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'element_texts';";
            string createSql = check.ExecuteScalar() as string ?? string.Empty;
            if (createSql.IndexOf("'prerequisites'", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            bool foreignKeysEnabled = ExecuteLongScalar(connection, "PRAGMA foreign_keys;") != 0;
            if (foreignKeysEnabled)
                ExecuteSql(connection, null, "PRAGMA foreign_keys = OFF;");

            DropViewsReferencingTable(connection, "element_texts");

            using var rebuild = connection.CreateCommand();
            rebuild.CommandText = @"
CREATE TABLE IF NOT EXISTS element_texts_new
(
    element_text_id INTEGER PRIMARY KEY,
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    text_kind TEXT NOT NULL CHECK
    (
        text_kind IN
        (
            'description',
            'sheet',
            'prerequisite',
            'prerequisites',
            'multiclass-prerequisite',
            'summary'
        )
    ),
    ordinal INTEGER NOT NULL DEFAULT 1,
    level INTEGER,
    display INTEGER CHECK (display IN (0, 1)),
    alt_text TEXT,
    action_text TEXT,
    usage_text TEXT,
    body TEXT NOT NULL
);

INSERT INTO element_texts_new
(
    element_text_id,
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
SELECT
    element_text_id,
    element_id,
    CASE
        WHEN text_kind IN ('description', 'sheet', 'prerequisite', 'prerequisites', 'multiclass-prerequisite', 'summary')
            THEN text_kind
        ELSE 'summary'
    END,
    ordinal,
    level,
    display,
    alt_text,
    action_text,
    usage_text,
    body
FROM element_texts;

DROP TABLE element_texts;
ALTER TABLE element_texts_new RENAME TO element_texts;";
            rebuild.ExecuteNonQuery();

            if (foreignKeysEnabled)
                ExecuteSql(connection, null, "PRAGMA foreign_keys = ON;");
        }

        private static void DropViewsReferencingTable(SqliteConnection connection, string tableName)
        {
            using var select = connection.CreateCommand();
            select.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type = 'view'
  AND sql IS NOT NULL
  AND instr(lower(sql), lower($table_name)) > 0;";
            select.Parameters.AddWithValue("$table_name", tableName);

            var viewNames = new List<string>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                    viewNames.Add(reader.GetString(0));
            }

            foreach (string viewName in viewNames)
            {
                using var drop = connection.CreateCommand();
                drop.CommandText = $"DROP VIEW IF EXISTS {QuoteIdentifier(viewName)};";
                drop.ExecuteNonQuery();
            }
        }

        private static bool EnsureColumnExistsIfTableExists(
            SqliteConnection connection,
            string tableName,
            string columnName,
            string columnDefinition)
        {
            if (!TableExists(connection, tableName))
                return false;

            return EnsureColumnExists(connection, tableName, columnName, columnDefinition);
        }

        private static long GetStoredDataVersion(SqliteConnection connection)
        {
            if (!TableExists(connection, "database_metadata"))
                return 0;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(data_version), 0) FROM database_metadata;";
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table_name;";
            check.Parameters.AddWithValue("$table_name", tableName);
            return (long)(check.ExecuteScalar() ?? 0L) != 0;
        }

        private static void EnsureDatabaseMetadataDataVersion(SqliteConnection connection, int dataVersion)
        {
            if (!TableExists(connection, "database_metadata"))
                return;

            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE database_metadata
SET data_version = $data_version
WHERE singleton_id = 1
  AND COALESCE(data_version, 0) < $data_version;";
            command.Parameters.AddWithValue("$data_version", dataVersion);
            command.ExecuteNonQuery();
        }

        private static void EnsureSpellcastingProfileEntriesSchema(SqliteConnection connection)
        {
            if (!TableExists(connection, "spellcasting_profiles"))
                return;

            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS spellcasting_profile_entries
(
    spellcasting_profile_entry_id INTEGER PRIMARY KEY,
    spellcasting_profile_id INTEGER NOT NULL REFERENCES spellcasting_profiles(spellcasting_profile_id) ON DELETE CASCADE,
    entry_kind TEXT NOT NULL CHECK (entry_kind IN ('list', 'extend')),
    ordinal INTEGER NOT NULL,
    entry_text TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_spellcasting_profile_entries_identity
    ON spellcasting_profile_entries(spellcasting_profile_id, entry_kind, ordinal, entry_text);
CREATE INDEX IF NOT EXISTS ix_spellcasting_profile_entries_profile
    ON spellcasting_profile_entries(spellcasting_profile_id, entry_kind, ordinal);
CREATE INDEX IF NOT EXISTS ix_spellcasting_profile_entries_text
    ON spellcasting_profile_entries(entry_kind, entry_text);";
            command.ExecuteNonQuery();
        }

        private static bool SpellcastingProfileEntriesNeedRebuild(SqliteConnection connection)
        {
            if (!TableExists(connection, "spellcasting_profiles") ||
                !TableExists(connection, "spellcasting_profile_entries"))
            {
                return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*)
FROM spellcasting_profiles AS sp
WHERE
(
    COALESCE(trim(sp.list_text), '') <> ''
    AND NOT EXISTS
    (
        SELECT 1
        FROM spellcasting_profile_entries AS spe
        WHERE spe.spellcasting_profile_id = sp.spellcasting_profile_id
          AND spe.entry_kind = 'list'
    )
)
OR
(
    COALESCE(trim(sp.extend_text), '') <> ''
    AND NOT EXISTS
    (
        SELECT 1
        FROM spellcasting_profile_entries AS spe
        WHERE spe.spellcasting_profile_id = sp.spellcasting_profile_id
          AND spe.entry_kind = 'extend'
    )
);";
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
        }

        private static void RebuildSpellcastingProfileEntries(SqliteConnection connection)
        {
            if (!TableExists(connection, "spellcasting_profiles") ||
                !TableExists(connection, "spellcasting_profile_entries"))
            {
                return;
            }

            var profiles = new List<(long SpellcastingProfileId, string ListText, string ExtendText)>();
            using var transaction = connection.BeginTransaction();
            ExecuteSql(connection, transaction, "DELETE FROM spellcasting_profile_entries;");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT
    spellcasting_profile_id,
    list_text,
    extend_text
FROM spellcasting_profiles;";

            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    profiles.Add((
                        reader.GetInt64(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }

            foreach (var profile in profiles)
            {
                InsertSpellcastingProfileEntries(connection, transaction, profile.SpellcastingProfileId, "list", profile.ListText);
                InsertSpellcastingProfileEntries(connection, transaction, profile.SpellcastingProfileId, "extend", profile.ExtendText);
            }

            transaction.Commit();
        }

        private static void InvalidateSourceFileHashes(SqliteConnection connection)
        {
            if (!TableExists(connection, "source_files"))
                return;

            // Older databases may already have imported XML content, but because the
            // new grant columns did not exist at the time, unchanged source files
            // would otherwise never be revisited. Clearing the stored hashes forces
            // the next XML import to reprocess every Aurora file and backfill the
            // new grant metadata.
            using var invalidateXmlHashes = connection.CreateCommand();
            invalidateXmlHashes.CommandText = "UPDATE source_files SET file_hash = NULL;";
            invalidateXmlHashes.ExecuteNonQuery();
        }

        private static bool EnsureColumnExists(
            SqliteConnection connection,
            string tableName,
            string columnName,
            string columnDefinition)
        {
            using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = $column_name;";
            check.Parameters.AddWithValue("$column_name", columnName);
            if ((long)(check.ExecuteScalar() ?? 0L) != 0)
                return false;

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
            alter.ExecuteNonQuery();
            return true;
        }

        private static void RefreshResolutionViews(SqliteConnection connection)
        {
            using var views = connection.CreateCommand();
            views.CommandText = @"
DROP VIEW IF EXISTS v_resolved_elements;
CREATE VIEW v_resolved_elements AS
SELECT
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
FROM resolved_elements_cache;

DROP VIEW IF EXISTS v_resolved_unique_element_names;
CREATE VIEW v_resolved_unique_element_names AS
SELECT
    normalized_name,
    winning_element_id,
    name
FROM resolved_unique_element_names_cache;

DROP VIEW IF EXISTS v_duplicate_aurora_ids;
CREATE VIEW v_duplicate_aurora_ids AS
WITH duplicate_ids AS
(
    SELECT
        aurora_id,
        COUNT(*) AS duplicate_count
    FROM elements
    WHERE aurora_id IS NOT NULL
      AND trim(aurora_id) <> ''
    GROUP BY aurora_id
    HAVING COUNT(*) > 1
)
SELECT
    e.aurora_id,
    e.element_id,
    e.name,
    et.type_name,
    sf.relative_path,
    cp.package_key,
    cp.package_name,
    cp.package_kind,
    cp.precedence_rank,
    COALESCE(cp.is_enabled, 1) AS is_enabled,
    duplicate_ids.duplicate_count,
    CASE
        WHEN rec.winning_element_id = e.element_id THEN 1
        ELSE 0
    END AS is_winner
FROM duplicate_ids
JOIN elements AS e
    ON e.aurora_id = duplicate_ids.aurora_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = e.source_file_id
LEFT JOIN content_packages AS cp
    ON cp.content_package_id = sf.content_package_id
LEFT JOIN resolved_elements_cache AS rec
    ON rec.aurora_id = e.aurora_id;";

            views.CommandText += @"

DROP VIEW IF EXISTS v_package_resolution_summary;
CREATE VIEW v_package_resolution_summary AS
WITH file_counts AS
(
    SELECT content_package_id, COUNT(*) AS file_count
    FROM source_files
    GROUP BY content_package_id
),
winner_counts AS
(
    SELECT content_package_id, COUNT(*) AS winning_element_count
    FROM resolved_elements_cache
    GROUP BY content_package_id
),
duplicate_counts AS
(
    SELECT
        sf.content_package_id,
        COUNT(*) AS duplicate_element_count,
        SUM(CASE WHEN dup.is_winner = 1 THEN 1 ELSE 0 END) AS duplicate_winner_count,
        SUM(CASE WHEN dup.is_winner = 0 THEN 1 ELSE 0 END) AS duplicate_loser_count
    FROM v_duplicate_aurora_ids AS dup
    JOIN source_files AS sf
        ON sf.relative_path = dup.relative_path
    GROUP BY sf.content_package_id
)
SELECT
    cp.content_package_id,
    cp.package_key,
    cp.package_name,
    cp.package_kind,
    cp.precedence_rank,
    cp.is_enabled,
    COALESCE(file_counts.file_count, 0) AS file_count,
    COALESCE(winner_counts.winning_element_count, 0) AS winning_element_count,
    COALESCE(duplicate_counts.duplicate_element_count, 0) AS duplicate_element_count,
    COALESCE(duplicate_counts.duplicate_winner_count, 0) AS duplicate_winner_count,
    COALESCE(duplicate_counts.duplicate_loser_count, 0) AS duplicate_loser_count
FROM content_packages AS cp
LEFT JOIN file_counts
    ON file_counts.content_package_id = cp.content_package_id
LEFT JOIN winner_counts
    ON winner_counts.content_package_id = cp.content_package_id
LEFT JOIN duplicate_counts
    ON duplicate_counts.content_package_id = cp.content_package_id;

DROP VIEW IF EXISTS v_unresolved_loader_links;
CREATE VIEW v_unresolved_loader_links AS
SELECT
    'grant' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS link_id,
    g.target_aurora_id AS unresolved_key,
    g.name_text AS unresolved_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE g.target_aurora_id IS NOT NULL
  AND g.target_element_id IS NULL
  AND COALESCE(g.target_semantic_key, '') = ''

UNION ALL

SELECT
    'extract-item' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(ei.extract_item_id AS TEXT) AS link_id,
    ei.target_aurora_id AS unresolved_key,
    ei.item_text AS unresolved_text
FROM element_extract_items AS ei
JOIN element_extracts AS ex
    ON ex.element_id = ei.element_id
JOIN elements AS owner
    ON owner.element_id = ex.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE ei.linked_element_id IS NULL
  AND (ei.target_aurora_id IS NOT NULL OR ei.item_text IS NOT NULL)

UNION ALL

SELECT
    'select-item' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(si.select_item_id AS TEXT) AS link_id,
    si.target_aurora_id AS unresolved_key,
    si.item_text AS unresolved_text
FROM select_items AS si
JOIN selects AS s
    ON s.select_id = si.select_id
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE si.linked_element_id IS NULL
  AND si.option_kind <> 'text-choice'
  AND (si.target_aurora_id IS NOT NULL OR si.item_text IS NOT NULL)

UNION ALL

SELECT
    'feature-parent' AS link_kind,
    feature.element_id AS owner_element_id,
    feature.aurora_id AS owner_aurora_id,
    feature.name AS owner_name,
    feature_type.type_name AS owner_type_name,
    CAST(feature.element_id AS TEXT) AS link_id,
    feature_meta.parent_support_text AS unresolved_key,
    feature_meta.parent_support_text AS unresolved_text
FROM features AS feature_meta
JOIN elements AS feature
    ON feature.element_id = feature_meta.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature.element_type_id
WHERE feature_meta.parent_support_text IS NOT NULL
  AND feature_meta.parent_element_id IS NULL

UNION ALL

SELECT
    'archetype-parent' AS link_kind,
    archetype.element_id AS owner_element_id,
    archetype.aurora_id AS owner_aurora_id,
    archetype.name AS owner_name,
    archetype_type.type_name AS owner_type_name,
    CAST(archetype.element_id AS TEXT) AS link_id,
    archetype_meta.parent_support_text AS unresolved_key,
    archetype_meta.parent_support_text AS unresolved_text
FROM archetypes AS archetype_meta
JOIN elements AS archetype
    ON archetype.element_id = archetype_meta.element_id
JOIN element_types AS archetype_type
    ON archetype_type.element_type_id = archetype.element_type_id
WHERE archetype_meta.parent_support_text IS NOT NULL
  AND archetype_meta.parent_class_element_id IS NULL;

DROP VIEW IF EXISTS v_unresolved_loader_link_diagnostics;
CREATE VIEW v_unresolved_loader_link_diagnostics AS
WITH background_file_counts AS
(
    SELECT
        bg.source_file_id,
        COUNT(*) AS background_count
    FROM backgrounds AS b
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    GROUP BY bg.source_file_id
),
feature_parent_family_counts AS
(
    SELECT
        unresolved_text,
        COUNT(*) AS family_count
    FROM v_unresolved_loader_links
    WHERE link_kind = 'feature-parent'
      AND unresolved_text IS NOT NULL
    GROUP BY unresolved_text
)
SELECT
    raw.link_kind,
    raw.owner_element_id,
    raw.owner_aurora_id,
    raw.owner_name,
    raw.owner_type_name,
    raw.link_id,
    raw.unresolved_key,
    raw.unresolved_text,
    CASE
        WHEN raw.link_kind = 'feature-parent'
         AND raw.unresolved_text = 'Background Feature'
         AND COALESCE(background_file_counts.background_count, 0) = 0
            THEN 'option-pool'
        WHEN raw.link_kind = 'feature-parent'
         AND
         (
             COALESCE(feature_parent_family_counts.family_count, 0) > 1
             OR raw.unresolved_text LIKE '%Option%'
             OR raw.unresolved_text LIKE 'PHB24 %'
             OR raw.unresolved_text LIKE 'Starry Form %'
             OR raw.unresolved_text LIKE 'Elemental Initiate %'
             OR raw.unresolved_text IN
                (
                    'BH Variant',
                    'MAgic of the Blade',
                    'Monster Type',
                    'Necromancer Variant Feature',
                    'Pactd Boon',
                    'vampire'
                )
         )
            THEN 'option-pool'
        WHEN raw.link_kind = 'archetype-parent'
         AND raw.unresolved_text = 'Training Paradigm'
            THEN 'missing-source'
        WHEN raw.link_kind = 'grant'
         AND COALESCE(trim(raw.unresolved_key), '') = ''
            THEN 'missing-source'
        ELSE 'actionable'
    END AS diagnostic_status,
    CASE
        WHEN raw.link_kind = 'feature-parent'
         AND raw.unresolved_text = 'Background Feature'
         AND COALESCE(background_file_counts.background_count, 0) = 0
            THEN 'background-feature-option-pool'
        WHEN raw.link_kind = 'feature-parent'
         AND
         (
             COALESCE(feature_parent_family_counts.family_count, 0) > 1
             OR raw.unresolved_text LIKE '%Option%'
             OR raw.unresolved_text LIKE 'PHB24 %'
             OR raw.unresolved_text LIKE 'Starry Form %'
             OR raw.unresolved_text LIKE 'Elemental Initiate %'
             OR raw.unresolved_text IN
                (
                    'BH Variant',
                    'MAgic of the Blade',
                    'Monster Type',
                    'Necromancer Variant Feature',
                    'Pactd Boon',
                    'vampire'
                )
         )
            THEN 'feature-family-option-pool'
        WHEN raw.link_kind = 'archetype-parent'
         AND raw.unresolved_text = 'Training Paradigm'
            THEN 'archetype-base-class-not-imported'
        WHEN raw.link_kind = 'grant'
         AND COALESCE(trim(raw.unresolved_key), '') = ''
            THEN 'grant-empty-target-id'
        ELSE NULL
    END AS diagnostic_reason
FROM v_unresolved_loader_links AS raw
LEFT JOIN elements AS owner
    ON owner.element_id = raw.owner_element_id
LEFT JOIN background_file_counts
    ON background_file_counts.source_file_id = owner.source_file_id
LEFT JOIN feature_parent_family_counts
    ON feature_parent_family_counts.unresolved_text = raw.unresolved_text;";

            views.CommandText += @"

DROP VIEW IF EXISTS v_source_integrity_issues;
CREATE VIEW v_source_integrity_issues AS
SELECT
    'grant-target-id-in-name-attribute' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS issue_key,
    COALESCE(
        NULLIF(trim(g.raw_xml), ''),
        '<grant type=""' || COALESCE(g.grant_type, '') || '"" name=""' || COALESCE(g.name_text, '') || '"" />'
    ) AS issue_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(g.target_aurora_id), '') <> ''
  AND COALESCE(trim(g.name_text), '') <> ''
  AND upper(trim(g.name_text)) LIKE 'ID\_%' ESCAPE '\'
  AND trim(g.target_aurora_id) = trim(g.name_text)

UNION ALL

SELECT
    'blank-grant-target-id' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS issue_key,
    COALESCE(
        NULLIF(trim(g.raw_xml), ''),
        '<grant type=""' || COALESCE(g.grant_type, '') || '"" id="""" />'
    ) AS issue_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(g.target_aurora_id), '') = ''
  AND COALESCE(trim(g.grant_type), '') <> ''

UNION ALL

SELECT
    'blank-select-type' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(s.select_id AS TEXT) AS issue_key,
    COALESCE(s.raw_xml, '<select />') AS issue_text
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(s.select_type), '') = ''

UNION ALL

SELECT
    'blank-stat-name' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(st.stat_id AS TEXT) AS issue_key,
    COALESCE(st.raw_xml, '<stat />') AS issue_text
FROM stats AS st
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = st.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(st.stat_name), '') = ''

UNION ALL

SELECT
    'duplicate-element-id-in-file' AS issue_kind,
    dup.source_file_id,
    sf.relative_path,
    NULL AS owner_element_id,
    NULL AS owner_aurora_id,
    NULL AS owner_name,
    NULL AS owner_type_name,
    dup.aurora_id AS issue_key,
    'duplicate-count=' || dup.duplicate_count AS issue_text
FROM
(
    SELECT
        source_file_id,
        aurora_id,
        COUNT(*) AS duplicate_count
    FROM elements
    WHERE COALESCE(trim(aurora_id), '') <> ''
    GROUP BY source_file_id, aurora_id
    HAVING COUNT(*) > 1
) AS dup
JOIN source_files AS sf
    ON sf.source_file_id = dup.source_file_id;";

            views.CommandText += @"

DROP VIEW IF EXISTS v_class_feature_progression;
CREATE VIEW v_class_feature_progression AS
SELECT
    c.element_id AS class_element_id,
    class_element.aurora_id AS class_aurora_id,
    class_element.name AS class_name,
    class_rec.package_key AS class_package_key,
    class_sf.relative_path AS class_source_path,
    c.hit_die,
    c.short_text AS class_short_text,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    COALESCE(g.grant_level, feature_meta.min_level) AS unlock_level,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    feature_type.type_name AS feature_type_name,
    feature_meta.parent_element_id,
    feature_meta.parent_support_text,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM classes AS c
JOIN resolved_elements_cache AS class_rec
    ON class_rec.winning_element_id = c.element_id
JOIN elements AS class_element
    ON class_element.element_id = c.element_id
JOIN source_files AS class_sf
    ON class_sf.source_file_id = class_element.source_file_id
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = c.element_id
   AND rs.scope_key = 'element'
JOIN grants AS g
    ON g.rule_scope_id = rs.rule_scope_id
JOIN elements AS feature_element
    ON feature_element.element_id = g.target_element_id
JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN features AS feature_meta
    ON feature_meta.element_id = feature_element.element_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_element.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_element.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_element.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE g.target_element_id IS NOT NULL
  AND feature_type.type_name = 'Class Feature';

DROP VIEW IF EXISTS v_class_archetype_slots;
CREATE VIEW v_class_archetype_slots AS
SELECT
    cfp.class_element_id,
    cfp.class_aurora_id,
    cfp.class_name,
    cfp.class_package_key,
    cfp.class_source_path,
    cfp.unlock_level,
    cfp.feature_element_id AS slot_feature_element_id,
    cfp.feature_aurora_id AS slot_feature_aurora_id,
    cfp.feature_name AS slot_feature_name,
    cfp.feature_package_key AS slot_feature_package_key,
    cfp.feature_source_path AS slot_feature_source_path,
    cfp.feature_summary_text AS slot_feature_summary_text,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.number_to_choose,
    s.is_optional
FROM v_class_feature_progression AS cfp
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = cfp.feature_element_id
   AND rs.scope_key = 'element'
JOIN selects AS s
    ON s.rule_scope_id = rs.rule_scope_id
WHERE s.select_type = 'Archetype';

DROP VIEW IF EXISTS v_archetype_feature_progression;
CREATE VIEW v_archetype_feature_progression AS
SELECT
    a.element_id AS archetype_element_id,
    archetype_element.aurora_id AS archetype_aurora_id,
    archetype_element.name AS archetype_name,
    archetype_rec.package_key AS archetype_package_key,
    archetype_sf.relative_path AS archetype_source_path,
    class_element.element_id AS class_element_id,
    class_element.aurora_id AS class_aurora_id,
    class_element.name AS class_name,
    class_rec.package_key AS class_package_key,
    class_sf.relative_path AS class_source_path,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    COALESCE(g.grant_level, feature_meta.min_level) AS unlock_level,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    feature_type.type_name AS feature_type_name,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM archetypes AS a
JOIN resolved_elements_cache AS archetype_rec
    ON archetype_rec.winning_element_id = a.element_id
JOIN elements AS archetype_element
    ON archetype_element.element_id = a.element_id
JOIN source_files AS archetype_sf
    ON archetype_sf.source_file_id = archetype_element.source_file_id
LEFT JOIN elements AS class_element
    ON class_element.element_id = a.parent_class_element_id
LEFT JOIN resolved_elements_cache AS class_rec
    ON class_rec.winning_element_id = class_element.element_id
LEFT JOIN source_files AS class_sf
    ON class_sf.source_file_id = class_element.source_file_id
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = a.element_id
   AND rs.scope_key = 'element'
JOIN grants AS g
    ON g.rule_scope_id = rs.rule_scope_id
JOIN elements AS feature_element
    ON feature_element.element_id = g.target_element_id
JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN features AS feature_meta
    ON feature_meta.element_id = feature_element.element_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_element.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_element.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_element.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE g.target_element_id IS NOT NULL
  AND feature_type.type_name = 'Archetype Feature';

DROP VIEW IF EXISTS v_background_core;
CREATE VIEW v_background_core AS
SELECT
    b.element_id AS background_element_id,
    background_element.aurora_id AS background_aurora_id,
    background_element.name AS background_name,
    background_rec.package_key AS background_package_key,
    background_sf.relative_path AS background_source_path,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    COALESCE(background_summary.body, background_sheet.body, background_description.body) AS background_summary_text,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM backgrounds AS b
JOIN resolved_elements_cache AS background_rec
    ON background_rec.winning_element_id = b.element_id
JOIN elements AS background_element
    ON background_element.element_id = b.element_id
JOIN source_files AS background_sf
    ON background_sf.source_file_id = background_element.source_file_id
LEFT JOIN element_texts AS background_summary
    ON background_summary.element_id = b.element_id
   AND background_summary.text_kind = 'summary'
   AND background_summary.ordinal = 1
LEFT JOIN element_texts AS background_sheet
    ON background_sheet.element_id = b.element_id
   AND background_sheet.text_kind = 'sheet'
   AND background_sheet.ordinal = 1
LEFT JOIN element_texts AS background_description
    ON background_description.element_id = b.element_id
   AND background_description.text_kind = 'description'
   AND background_description.ordinal = 1
LEFT JOIN features AS feature_meta
    ON feature_meta.parent_element_id = b.element_id
   AND feature_meta.feature_kind = 'Background Feature'
LEFT JOIN elements AS feature_element
    ON feature_element.element_id = feature_meta.element_id
LEFT JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
LEFT JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_meta.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_meta.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_meta.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE feature_element.element_id IS NULL
   OR feature_rec.winning_element_id = feature_element.element_id;

DROP VIEW IF EXISTS v_race_core;
CREATE VIEW v_race_core AS
SELECT
    r.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(r.names_format_text, '') AS names_format_text,
    COALESCE(race_summary.body, race_sheet.body, race_description.body) AS race_summary_text,
    (
        SELECT COUNT(*)
        FROM subraces AS sr
        JOIN resolved_elements_cache AS sr_rec
            ON sr_rec.winning_element_id = sr.element_id
        WHERE sr.race_element_id = r.element_id
    ) AS subrace_count,
    (
        SELECT COUNT(*)
        FROM race_variants AS rv
        JOIN resolved_elements_cache AS rv_rec
            ON rv_rec.winning_element_id = rv.element_id
        WHERE rv.race_element_id = r.element_id
    ) AS variant_count,
    (
        SELECT COUNT(*)
        FROM features AS f
        JOIN resolved_elements_cache AS feature_rec
            ON feature_rec.winning_element_id = f.element_id
        WHERE f.parent_element_id = r.element_id
          AND f.feature_kind IN ('Racial Trait', 'Dragonmark')
    ) AS racial_trait_count
FROM races AS r
JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = r.element_id
JOIN elements AS race_element
    ON race_element.element_id = r.element_id
JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS race_summary
    ON race_summary.element_id = r.element_id
   AND race_summary.text_kind = 'summary'
   AND race_summary.ordinal = 1
LEFT JOIN element_texts AS race_sheet
    ON race_sheet.element_id = r.element_id
   AND race_sheet.text_kind = 'sheet'
   AND race_sheet.ordinal = 1
LEFT JOIN element_texts AS race_description
    ON race_description.element_id = r.element_id
   AND race_description.text_kind = 'description'
   AND race_description.ordinal = 1;

DROP VIEW IF EXISTS v_subrace_core;
CREATE VIEW v_subrace_core AS
SELECT
    sr.element_id AS subrace_element_id,
    subrace_element.aurora_id AS subrace_aurora_id,
    subrace_element.name AS subrace_name,
    subrace_rec.package_key AS subrace_package_key,
    subrace_sf.relative_path AS subrace_source_path,
    race_element.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(subrace_summary.body, subrace_sheet.body, subrace_description.body) AS subrace_summary_text
FROM subraces AS sr
JOIN resolved_elements_cache AS subrace_rec
    ON subrace_rec.winning_element_id = sr.element_id
JOIN elements AS subrace_element
    ON subrace_element.element_id = sr.element_id
JOIN source_files AS subrace_sf
    ON subrace_sf.source_file_id = subrace_element.source_file_id
LEFT JOIN elements AS race_element
    ON race_element.element_id = sr.race_element_id
LEFT JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = race_element.element_id
LEFT JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS subrace_summary
    ON subrace_summary.element_id = sr.element_id
   AND subrace_summary.text_kind = 'summary'
   AND subrace_summary.ordinal = 1
LEFT JOIN element_texts AS subrace_sheet
    ON subrace_sheet.element_id = sr.element_id
   AND subrace_sheet.text_kind = 'sheet'
   AND subrace_sheet.ordinal = 1
LEFT JOIN element_texts AS subrace_description
    ON subrace_description.element_id = sr.element_id
   AND subrace_description.text_kind = 'description'
   AND subrace_description.ordinal = 1;

DROP VIEW IF EXISTS v_race_variant_core;
CREATE VIEW v_race_variant_core AS
SELECT
    rv.element_id AS variant_element_id,
    variant_element.aurora_id AS variant_aurora_id,
    variant_element.name AS variant_name,
    variant_rec.package_key AS variant_package_key,
    variant_sf.relative_path AS variant_source_path,
    race_element.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(variant_summary.body, variant_sheet.body, variant_description.body) AS variant_summary_text
FROM race_variants AS rv
JOIN resolved_elements_cache AS variant_rec
    ON variant_rec.winning_element_id = rv.element_id
JOIN elements AS variant_element
    ON variant_element.element_id = rv.element_id
JOIN source_files AS variant_sf
    ON variant_sf.source_file_id = variant_element.source_file_id
LEFT JOIN elements AS race_element
    ON race_element.element_id = rv.race_element_id
LEFT JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = race_element.element_id
LEFT JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS variant_summary
    ON variant_summary.element_id = rv.element_id
   AND variant_summary.text_kind = 'summary'
   AND variant_summary.ordinal = 1
LEFT JOIN element_texts AS variant_sheet
    ON variant_sheet.element_id = rv.element_id
   AND variant_sheet.text_kind = 'sheet'
   AND variant_sheet.ordinal = 1
LEFT JOIN element_texts AS variant_description
    ON variant_description.element_id = rv.element_id
   AND variant_description.text_kind = 'description'
   AND variant_description.ordinal = 1;

DROP VIEW IF EXISTS v_granted_proficiencies;
CREATE VIEW v_granted_proficiencies AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    g.grant_level,
    proficiency.element_id AS proficiency_element_id,
    proficiency.aurora_id AS proficiency_aurora_id,
    proficiency.name AS proficiency_name,
    proficiency_rec.package_key AS proficiency_package_key,
    proficiency_sf.relative_path AS proficiency_source_path
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN elements AS proficiency
    ON proficiency.element_id = g.target_element_id
JOIN resolved_elements_cache AS proficiency_rec
    ON proficiency_rec.winning_element_id = proficiency.element_id
JOIN source_files AS proficiency_sf
    ON proficiency_sf.source_file_id = proficiency.source_file_id
JOIN element_types AS proficiency_type
    ON proficiency_type.element_type_id = proficiency.element_type_id
WHERE proficiency_type.type_name = 'Proficiency';

DROP VIEW IF EXISTS v_granted_languages;
CREATE VIEW v_granted_languages AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    g.grant_level,
    language.element_id AS language_element_id,
    language.aurora_id AS language_aurora_id,
    language.name AS language_name,
    language_rec.package_key AS language_package_key,
    language_sf.relative_path AS language_source_path
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN elements AS language
    ON language.element_id = g.target_element_id
JOIN resolved_elements_cache AS language_rec
    ON language_rec.winning_element_id = language.element_id
JOIN source_files AS language_sf
    ON language_sf.source_file_id = language.source_file_id
JOIN element_types AS language_type
    ON language_type.element_type_id = language.element_type_id
WHERE language_type.type_name = 'Language';

DROP VIEW IF EXISTS v_selectable_options;
CREATE VIEW v_selectable_options AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    'element' AS option_kind,
    option_element.element_id AS option_element_id,
    option_element.aurora_id AS option_aurora_id,
    option_element.name AS option_name,
    option_rec.package_key AS option_package_key,
    option_sf.relative_path AS option_source_path,
    option_type.type_name AS option_type_name,
    NULL AS option_text,
    GROUP_CONCAT(DISTINCT sol.match_kind) AS match_kinds,
    GROUP_CONCAT(DISTINCT st.support_text) AS support_tags
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN select_option_links AS sol
    ON sol.select_id = s.select_id
JOIN elements AS option_element
    ON option_element.element_id = sol.option_element_id
JOIN resolved_elements_cache AS option_rec
    ON option_rec.winning_element_id = option_element.element_id
JOIN source_files AS option_sf
    ON option_sf.source_file_id = option_element.source_file_id
JOIN element_types AS option_type
    ON option_type.element_type_id = option_element.element_type_id
LEFT JOIN support_tags AS st
    ON st.support_tag_id = sol.support_tag_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_rec.package_key,
    owner_sf.relative_path,
    owner_type.type_name,
    s.select_id,
    s.name_text,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    option_element.element_id,
    option_element.aurora_id,
    option_element.name,
    option_rec.package_key,
    option_sf.relative_path,
    option_type.type_name

UNION ALL

SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    'text-choice' AS option_kind,
    NULL AS option_element_id,
    NULL AS option_aurora_id,
    NULL AS option_name,
    NULL AS option_package_key,
    NULL AS option_source_path,
    NULL AS option_type_name,
    si.item_text AS option_text,
    NULL AS match_kinds,
    NULL AS support_tags
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN select_items AS si
    ON si.select_id = s.select_id
WHERE si.option_kind = 'text-choice';";
            views.ExecuteNonQuery();
        }

        private static void RefreshAppContractViews(SqliteConnection connection)
        {
            using var views = connection.CreateCommand();
            views.CommandText = @"
DROP VIEW IF EXISTS v_choice_templates;
CREATE VIEW v_choice_templates AS
WITH option_counts AS
(
    SELECT
        s.select_id,
        COUNT(DISTINCT sol.option_element_id) AS element_option_count,
        SUM(CASE WHEN si.option_kind = 'text-choice' THEN 1 ELSE 0 END) AS text_option_count
    FROM selects AS s
    LEFT JOIN select_option_links AS sol
        ON sol.select_id = s.select_id
    LEFT JOIN select_items AS si
        ON si.select_id = s.select_id
    GROUP BY s.select_id
)
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.supports_text,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    s.requirements_text,
    CASE
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'spell' THEN 'broad-spell-pool'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'language' THEN 'broad-language-pool'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency' THEN 'broad-proficiency-pool'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'feat' THEN 'broad-feat-pool'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'list' THEN 'text-choice-pool'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'class feature'
         AND lower(COALESCE(s.supports_text, '')) LIKE '%improvement option%'
            THEN 'asi-feature-pool'
        ELSE 'fixed-element-pool'
    END AS select_policy,
    CASE
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'spell' THEN 'spell-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'language' THEN 'language-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%skill%' OR lower(COALESCE(s.supports_text, '')) LIKE '%skill%')
            THEN 'skill-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%tool%' OR lower(COALESCE(s.supports_text, '')) LIKE '%tool%')
            THEN 'tool-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%armor%' OR lower(COALESCE(s.supports_text, '')) LIKE '%armor%')
            THEN 'armor-proficiency-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%weapon%' OR lower(COALESCE(s.supports_text, '')) LIKE '%weapon%')
            THEN 'weapon-proficiency-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%saving throw%' OR lower(COALESCE(s.supports_text, '')) LIKE '%saving throw%')
            THEN 'saving-throw-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'proficiency' THEN 'proficiency-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'feat' THEN 'feat-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'list' THEN 'text-choice'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'race variant' THEN 'race-variant-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'class feature'
         AND lower(COALESCE(s.supports_text, '')) LIKE '%improvement option%'
            THEN 'asi-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'class feature'
         AND (lower(COALESCE(s.name_text, '')) LIKE '%fighting style%' OR lower(COALESCE(s.supports_text, '')) LIKE '%fighting style%')
            THEN 'fighting-style-pick'
        WHEN lower(trim(COALESCE(s.select_type, ''))) = 'class feature' THEN 'feature-pick'
        ELSE 'generic-element-pick'
    END AS choice_family,
    COALESCE(option_counts.element_option_count, 0) AS element_option_count,
    COALESCE(option_counts.text_option_count, 0) AS text_option_count,
    COALESCE(option_counts.element_option_count, 0) + COALESCE(option_counts.text_option_count, 0) AS total_option_count
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN option_counts
    ON option_counts.select_id = s.select_id
WHERE rs.owner_kind = 'element';

DROP VIEW IF EXISTS v_app_choice_rows;
CREATE VIEW v_app_choice_rows AS
SELECT
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_type_name,
    owner_package_key,
    owner_source_path,
    lower(
        COALESCE(owner_aurora_id, owner_name) || '|' ||
        COALESCE(owner_package_key, '') || '|' ||
        COALESCE(owner_source_path, '') || '|' ||
        COALESCE(select_name, '') || '|' ||
        COALESCE(choice_family, '') || '|' ||
        COALESCE(select_type, '') || '|' ||
        COALESCE(CAST(select_level AS TEXT), '') || '|' ||
        CAST(number_to_choose AS TEXT) || '|' ||
        CAST(is_optional AS TEXT)
    ) AS choice_key,
    lower(
        COALESCE(owner_aurora_id, owner_name) || '|' ||
        COALESCE(owner_type_name, '') || '|' ||
        COALESCE(owner_package_key, '') || '|' ||
        COALESCE(owner_source_path, '') || '|' ||
        COALESCE(select_name, '') || '|' ||
        COALESCE(choice_family, '') || '|' ||
        COALESCE(select_type, '') || '|' ||
        COALESCE(CAST(select_level AS TEXT), '') || '|' ||
        CAST(number_to_choose AS TEXT) || '|' ||
        CAST(is_optional AS TEXT) || '|' ||
        COALESCE(select_policy, '') || '|' ||
        COALESCE(supports_text, '') || '|' ||
        COALESCE(requirements_text, '')
    ) AS choice_row_key,
    choice_family,
    select_policy,
    select_name,
    select_type,
    supports_text,
    select_level,
    number_to_choose,
    is_optional,
    requirements_text,
    CASE
        WHEN MIN(total_option_count) > 0 THEN 'static-template'
        WHEN lower(trim(COALESCE(select_policy, ''))) IN ('broad-spell-pool', 'broad-language-pool', 'broad-proficiency-pool', 'broad-feat-pool', 'asi-feature-pool')
            THEN 'runtime-semantic'
        WHEN lower(trim(COALESCE(choice_family, ''))) IN ('feature-pick', 'generic-element-pick', 'fighting-style-pick', 'race-variant-pick')
            THEN 'runtime-derived'
        ELSE 'empty-template'
    END AS option_count_kind,
    CASE
        WHEN MIN(total_option_count) > 0 THEN 1
        ELSE 0
    END AS is_static_option_count_complete,
    CASE
        WHEN MIN(total_option_count) > 0 THEN 0
        WHEN lower(trim(COALESCE(select_policy, ''))) IN ('broad-spell-pool', 'broad-language-pool', 'broad-proficiency-pool', 'broad-feat-pool', 'asi-feature-pool')
            THEN 1
        WHEN lower(trim(COALESCE(choice_family, ''))) IN ('feature-pick', 'generic-element-pick', 'fighting-style-pick', 'race-variant-pick')
            THEN 1
        ELSE 0
    END AS requires_runtime_option_resolution,
    MIN(element_option_count) AS static_element_option_count,
    MIN(text_option_count) AS static_text_option_count,
    MIN(total_option_count) AS static_total_option_count,
    MIN(element_option_count) AS element_option_count,
    MIN(text_option_count) AS text_option_count,
    MIN(total_option_count) AS total_option_count,
    COUNT(*) AS template_row_count,
    COUNT(DISTINCT select_id) AS select_id_count,
    MIN(select_id) AS min_select_id,
    GROUP_CONCAT(select_id) AS select_ids
FROM v_choice_templates
GROUP BY
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_type_name,
    owner_package_key,
    owner_source_path,
    choice_family,
    select_policy,
    select_name,
    select_type,
    supports_text,
    select_level,
    number_to_choose,
    is_optional,
    requirements_text;

DROP VIEW IF EXISTS v_granted_spells;
CREATE VIEW v_granted_spells AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    g.grant_level,
    g.spellcasting_name,
    g.is_prepared,
    g.requirements_text,
    spell.element_id AS spell_element_id,
    spell.aurora_id AS spell_aurora_id,
    spell.name AS spell_name,
    spell_rec.package_key AS spell_package_key,
    spell_sf.relative_path AS spell_source_path
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN elements AS spell
    ON spell.element_id = g.target_element_id
JOIN resolved_elements_cache AS spell_rec
    ON spell_rec.winning_element_id = spell.element_id
JOIN source_files AS spell_sf
    ON spell_sf.source_file_id = spell.source_file_id
JOIN element_types AS spell_type
    ON spell_type.element_type_id = spell.element_type_id
WHERE spell_type.type_name = 'Spell';

DROP VIEW IF EXISTS v_movement_effect_templates;
CREATE VIEW v_movement_effect_templates AS
WITH RECURSIVE
movement_stat_rows AS
(
    SELECT
        owner.element_id AS owner_element_id,
        owner.aurora_id AS owner_aurora_id,
        owner.name AS owner_name,
        owner_rec.package_key AS owner_package_key,
        owner_sf.relative_path AS owner_source_path,
        owner_type.type_name AS owner_type_name,
        st.stat_id AS source_row_id,
        lower(trim(st.stat_name)) AS raw_name,
        trim(COALESCE(NULLIF(st.value_expression_text, ''), NULLIF(st.inline_display, ''), NULLIF(st.alt_text, ''), NULLIF(st.bonus_expression_text, ''))) AS raw_value,
        st.stat_level AS grant_level
    FROM stats AS st
    JOIN rule_scopes AS rs
        ON rs.rule_scope_id = st.rule_scope_id
    JOIN elements AS owner
        ON owner.element_id = rs.owner_element_id
    JOIN resolved_elements_cache AS owner_rec
        ON owner_rec.winning_element_id = owner.element_id
    JOIN source_files AS owner_sf
        ON owner_sf.source_file_id = owner.source_file_id
    JOIN element_types AS owner_type
        ON owner_type.element_type_id = owner.element_type_id
    WHERE lower(st.stat_name) LIKE '%speed%'
      AND COALESCE(trim(COALESCE(NULLIF(st.value_expression_text, ''), NULLIF(st.inline_display, ''), NULLIF(st.alt_text, ''), NULLIF(st.bonus_expression_text, ''))), '') <> ''
),
movement_alias_map AS
(
    SELECT
        owner_element_id,
        raw_name,
        MAX(raw_value) AS raw_value
    FROM movement_stat_rows
    GROUP BY owner_element_id, raw_name
),
resolved_movement_stats AS
(
    SELECT
        msr.owner_element_id,
        msr.owner_aurora_id,
        msr.owner_name,
        msr.owner_package_key,
        msr.owner_source_path,
        msr.owner_type_name,
        msr.source_row_id,
        msr.raw_name,
        msr.raw_value AS resolved_value,
        msr.grant_level,
        0 AS depth
    FROM movement_stat_rows AS msr

    UNION ALL

    SELECT
        rms.owner_element_id,
        rms.owner_aurora_id,
        rms.owner_name,
        rms.owner_package_key,
        rms.owner_source_path,
        rms.owner_type_name,
        rms.source_row_id,
        rms.raw_name,
        trim(alias.raw_value) AS resolved_value,
        rms.grant_level,
        rms.depth + 1
    FROM resolved_movement_stats AS rms
    JOIN movement_alias_map AS alias
        ON alias.owner_element_id = rms.owner_element_id
       AND alias.raw_name = lower(trim(rms.resolved_value))
    WHERE rms.depth < 8
      AND COALESCE(trim(alias.raw_value), '') <> ''
      AND lower(trim(alias.raw_value)) <> lower(trim(rms.resolved_value))
),
final_movement_stats AS
(
    SELECT
        rms.owner_element_id,
        rms.owner_aurora_id,
        rms.owner_name,
        rms.owner_package_key,
        rms.owner_source_path,
        rms.owner_type_name,
        rms.source_row_id,
        rms.raw_name,
        rms.resolved_value,
        rms.grant_level
    FROM resolved_movement_stats AS rms
    LEFT JOIN movement_alias_map AS alias
        ON alias.owner_element_id = rms.owner_element_id
       AND alias.raw_name = lower(trim(rms.resolved_value))
       AND COALESCE(trim(alias.raw_value), '') <> ''
       AND lower(trim(alias.raw_value)) <> lower(trim(rms.resolved_value))
    WHERE alias.raw_name IS NULL
),
movement_sources AS
(
    SELECT
        fms.owner_element_id,
        fms.owner_aurora_id,
        fms.owner_name,
        fms.owner_package_key,
        fms.owner_source_path,
        fms.owner_type_name,
        'stat' AS source_kind,
        'stat:' || fms.source_row_id AS source_row_key,
        fms.raw_name,
        trim(fms.resolved_value) AS raw_value,
        fms.grant_level
    FROM final_movement_stats AS fms

    UNION ALL

    SELECT
        owner.element_id AS owner_element_id,
        owner.aurora_id AS owner_aurora_id,
        owner.name AS owner_name,
        owner_rec.package_key AS owner_package_key,
        owner_sf.relative_path AS owner_source_path,
        owner_type.type_name AS owner_type_name,
        'setter' AS source_kind,
        'setter:' || se.setter_entry_id AS source_row_key,
        lower(trim(se.setter_name)) AS raw_name,
        trim(se.setter_value) AS raw_value,
        NULL AS grant_level
    FROM setter_entries AS se
    JOIN setter_scopes AS ss
        ON ss.setter_scope_id = se.setter_scope_id
    JOIN elements AS owner
        ON owner.element_id = ss.owner_element_id
    JOIN resolved_elements_cache AS owner_rec
        ON owner_rec.winning_element_id = owner.element_id
    JOIN source_files AS owner_sf
        ON owner_sf.source_file_id = owner.source_file_id
    JOIN element_types AS owner_type
        ON owner_type.element_type_id = owner.element_type_id
    WHERE lower(se.setter_name) LIKE '%speed%'
      AND COALESCE(trim(se.setter_value), '') <> ''
),
movement_segments AS
(
    SELECT
        ms.owner_element_id,
        ms.owner_aurora_id,
        ms.owner_name,
        ms.owner_package_key,
        ms.owner_source_path,
        ms.owner_type_name,
        ms.source_kind,
        ms.source_row_key,
        ms.raw_name,
        ms.grant_level,
        trim(CASE WHEN instr(ms.raw_value, ',') > 0 THEN substr(ms.raw_value, 1, instr(ms.raw_value, ',') - 1) ELSE ms.raw_value END) AS segment,
        trim(CASE WHEN instr(ms.raw_value, ',') > 0 THEN substr(ms.raw_value, instr(ms.raw_value, ',') + 1) ELSE '' END) AS remainder
    FROM movement_sources AS ms

    UNION ALL

    SELECT
        seg.owner_element_id,
        seg.owner_aurora_id,
        seg.owner_name,
        seg.owner_package_key,
        seg.owner_source_path,
        seg.owner_type_name,
        seg.source_kind,
        seg.source_row_key,
        seg.raw_name,
        seg.grant_level,
        trim(CASE WHEN instr(seg.remainder, ',') > 0 THEN substr(seg.remainder, 1, instr(seg.remainder, ',') - 1) ELSE seg.remainder END) AS segment,
        trim(CASE WHEN instr(seg.remainder, ',') > 0 THEN substr(seg.remainder, instr(seg.remainder, ',') + 1) ELSE '' END) AS remainder
    FROM movement_segments AS seg
    WHERE COALESCE(seg.remainder, '') <> ''
),
normalized_movement_segments AS
(
    SELECT
        seg.owner_element_id,
        seg.owner_aurora_id,
        seg.owner_name,
        seg.owner_package_key,
        seg.owner_source_path,
        seg.owner_type_name,
        seg.source_kind,
        seg.source_row_key,
        seg.grant_level,
        CASE
            WHEN lower(seg.segment) LIKE 'fly %' THEN 'fly'
            WHEN lower(seg.segment) LIKE 'swim %' THEN 'swim'
            WHEN lower(seg.segment) LIKE 'climb %' THEN 'climb'
            WHEN lower(seg.segment) LIKE 'burrow %' THEN 'burrow'
            WHEN seg.raw_name LIKE '%fly%' THEN 'fly'
            WHEN seg.raw_name LIKE '%swim%' THEN 'swim'
            WHEN seg.raw_name LIKE '%climb%' THEN 'climb'
            WHEN seg.raw_name LIKE '%burrow%' THEN 'burrow'
            ELSE 'walk'
        END AS effect_subkind,
        trim(CASE
            WHEN lower(seg.segment) LIKE 'fly %' THEN substr(seg.segment, 5)
            WHEN lower(seg.segment) LIKE 'swim %' THEN substr(seg.segment, 6)
            WHEN lower(seg.segment) LIKE 'climb %' THEN substr(seg.segment, 7)
            WHEN lower(seg.segment) LIKE 'burrow %' THEN substr(seg.segment, 8)
            ELSE seg.segment
        END) AS raw_effect_value_text
    FROM movement_segments AS seg
    WHERE COALESCE(seg.segment, '') <> ''
),
clean_movement_rows AS
(
    SELECT
        nms.owner_element_id,
        nms.owner_aurora_id,
        nms.owner_name,
        nms.owner_package_key,
        nms.owner_source_path,
        nms.owner_type_name,
        nms.source_kind,
        nms.source_row_key,
        nms.grant_level,
        nms.effect_subkind,
        CASE
            WHEN nms.effect_subkind = 'fly' THEN 'Fly Speed'
            WHEN nms.effect_subkind = 'swim' THEN 'Swim Speed'
            WHEN nms.effect_subkind = 'climb' THEN 'Climb Speed'
            WHEN nms.effect_subkind = 'burrow' THEN 'Burrow Speed'
            ELSE 'Speed'
        END AS effect_name,
        trim(replace(replace(
            CASE
                WHEN instr(lower(nms.raw_effect_value_text), '(') > 0
                    THEN substr(lower(nms.raw_effect_value_text), 1, instr(lower(nms.raw_effect_value_text), '(') - 1)
                ELSE lower(nms.raw_effect_value_text)
            END,
            'ft.', ''),
            'ft', '')) AS effect_value_text
    FROM normalized_movement_segments AS nms
),
movement_numeric_rows AS
(
    SELECT
        cmr.owner_element_id,
        cmr.owner_aurora_id,
        cmr.owner_name,
        cmr.owner_package_key,
        cmr.owner_source_path,
        cmr.owner_type_name,
        'movement' AS effect_kind,
        cmr.effect_subkind,
        'movement:' || cmr.effect_subkind || ':' || cmr.effect_value_text AS effect_key,
        cmr.effect_name,
        cmr.effect_value_text,
        CAST(cmr.effect_value_text AS REAL) AS effect_numeric_value,
        cmr.owner_package_key AS effect_package_key,
        cmr.source_kind,
        cmr.source_row_key,
        NULL AS spellcasting_name,
        NULL AS is_prepared,
        cmr.grant_level
    FROM clean_movement_rows AS cmr
    WHERE cmr.effect_value_text GLOB '[0-9]*'
      AND CAST(cmr.effect_value_text AS REAL) > 0
)
SELECT DISTINCT
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    effect_kind,
    effect_subkind,
    effect_key,
    effect_name,
    effect_value_text,
    effect_numeric_value,
    effect_package_key,
    source_kind,
    source_row_key,
    spellcasting_name,
    is_prepared,
    grant_level
FROM movement_numeric_rows;

DROP VIEW IF EXISTS v_effect_templates;
CREATE VIEW v_effect_templates AS
SELECT
    gp.owner_element_id,
    gp.owner_aurora_id,
    gp.owner_name,
    gp.owner_package_key,
    gp.owner_source_path,
    gp.owner_type_name,
    'proficiency' AS effect_kind,
    CASE
        WHEN lower(gp.proficiency_name) LIKE '%skill%' THEN 'skill'
        WHEN lower(gp.proficiency_name) LIKE '%tool%' THEN 'tool'
        WHEN lower(gp.proficiency_name) LIKE '%armor%' THEN 'armor'
        WHEN lower(gp.proficiency_name) LIKE '%weapon%' THEN 'weapon'
        WHEN lower(gp.proficiency_name) LIKE '%saving throw%' THEN 'saving-throw'
        ELSE 'proficiency'
    END AS effect_subkind,
    COALESCE(gp.proficiency_aurora_id, 'proficiency:' || gp.proficiency_name) AS effect_key,
    gp.proficiency_name AS effect_name,
    gp.proficiency_name AS effect_value_text,
    NULL AS effect_numeric_value,
    gp.proficiency_package_key AS effect_package_key,
    'grant' AS source_kind,
    'grant:' || gp.grant_id AS source_row_key,
    NULL AS spellcasting_name,
    NULL AS is_prepared,
    gp.grant_level
FROM v_granted_proficiencies AS gp

UNION ALL

SELECT
    gl.owner_element_id,
    gl.owner_aurora_id,
    gl.owner_name,
    gl.owner_package_key,
    gl.owner_source_path,
    gl.owner_type_name,
    'language' AS effect_kind,
    'language' AS effect_subkind,
    COALESCE(gl.language_aurora_id, 'language:' || gl.language_name) AS effect_key,
    gl.language_name AS effect_name,
    gl.language_name AS effect_value_text,
    NULL AS effect_numeric_value,
    gl.language_package_key AS effect_package_key,
    'grant' AS source_kind,
    'grant:' || gl.grant_id AS source_row_key,
    NULL AS spellcasting_name,
    NULL AS is_prepared,
    gl.grant_level
FROM v_granted_languages AS gl

UNION ALL

SELECT
    gs.owner_element_id,
    gs.owner_aurora_id,
    gs.owner_name,
    gs.owner_package_key,
    gs.owner_source_path,
    gs.owner_type_name,
    'spell-grant' AS effect_kind,
    CASE WHEN gs.is_prepared = 1 THEN 'prepared-spell' ELSE 'spell' END AS effect_subkind,
    'spell:' || COALESCE(gs.spell_aurora_id, gs.spell_name) || '|' || COALESCE(gs.spellcasting_name, '') || '|prepared=' || COALESCE(gs.is_prepared, '') AS effect_key,
    gs.spell_name AS effect_name,
    gs.spellcasting_name AS effect_value_text,
    CAST(gs.grant_level AS REAL) AS effect_numeric_value,
    gs.spell_package_key AS effect_package_key,
    'grant' AS source_kind,
    'grant:' || gs.grant_id AS source_row_key,
    gs.spellcasting_name,
    gs.is_prepared,
    gs.grant_level
FROM v_granted_spells AS gs

UNION ALL

SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    CASE
        WHEN lower(trim(g.target_semantic_kind)) = 'size' THEN 'size'
        ELSE 'trait'
    END AS effect_kind,
    lower(replace(trim(COALESCE(g.target_semantic_kind, 'semantic-trait')), ' ', '-')) AS effect_subkind,
    COALESCE(g.target_semantic_key, 'semantic:' || COALESCE(g.target_semantic_name, g.name_text, g.target_aurora_id, g.grant_id)) AS effect_key,
    COALESCE(g.target_semantic_name, g.name_text, g.target_aurora_id) AS effect_name,
    COALESCE(g.target_semantic_name, g.name_text, g.target_aurora_id) AS effect_value_text,
    NULL AS effect_numeric_value,
    owner_rec.package_key AS effect_package_key,
    'semantic-grant' AS source_kind,
    'grant:' || g.grant_id AS source_row_key,
    NULL AS spellcasting_name,
    NULL AS is_prepared,
    g.grant_level
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE COALESCE(trim(g.target_semantic_kind), '') <> ''
   OR COALESCE(trim(g.target_semantic_name), '') <> ''

UNION ALL

SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    'sense' AS effect_kind,
    'vision' AS effect_subkind,
    COALESCE(g.target_aurora_id, 'sense:' || COALESCE(target.name, g.name_text, g.grant_id)) AS effect_key,
    COALESCE(target.name, g.name_text, g.target_semantic_name, g.target_aurora_id) AS effect_name,
    COALESCE(target.name, g.name_text, g.target_semantic_name, g.target_aurora_id) AS effect_value_text,
    NULL AS effect_numeric_value,
    owner_rec.package_key AS effect_package_key,
    'grant' AS source_kind,
    'grant:' || g.grant_id AS source_row_key,
    NULL AS spellcasting_name,
    NULL AS is_prepared,
    g.grant_level
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN elements AS target
    ON target.element_id = g.target_element_id
LEFT JOIN element_types AS target_type
    ON target_type.element_type_id = target.element_type_id
WHERE (g.grant_type = 'Vision' OR target_type.type_name = 'Vision')
  AND COALESCE(trim(g.target_semantic_kind), '') = ''
  AND COALESCE(trim(g.target_semantic_name), '') = ''

UNION ALL

SELECT
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    effect_kind,
    effect_subkind,
    effect_key,
    effect_name,
    effect_value_text,
    effect_numeric_value,
    effect_package_key,
    source_kind,
    source_row_key,
    spellcasting_name,
    is_prepared,
    grant_level
FROM v_movement_effect_templates;

DROP VIEW IF EXISTS v_spellcasting_profiles;
CREATE VIEW v_spellcasting_profiles AS
SELECT
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    trim(spellcasting_name) AS spellcasting_name,
    COUNT(*) AS granted_spell_count,
    SUM(CASE WHEN is_prepared = 1 THEN 1 ELSE 0 END) AS prepared_spell_count,
    SUM(CASE WHEN is_prepared = 0 THEN 1 ELSE 0 END) AS unprepared_spell_count
FROM v_granted_spells
WHERE NULLIF(trim(spellcasting_name), '') IS NOT NULL
GROUP BY
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    trim(spellcasting_name);

DROP VIEW IF EXISTS v_spellcasting_profile_entries;
CREATE VIEW v_spellcasting_profile_entries AS
SELECT
    sp.spellcasting_profile_id,
    sp.owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    sp.owner_kind,
    sp.profile_name,
    sp.ability_name,
    sp.is_extended,
    sp.prepare_spells,
    sp.allow_replace,
    spe.entry_kind,
    spe.ordinal AS entry_ordinal,
    spe.entry_text
FROM spellcasting_profile_entries AS spe
JOIN spellcasting_profiles AS sp
    ON sp.spellcasting_profile_id = spe.spellcasting_profile_id
JOIN elements AS owner
    ON owner.element_id = sp.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id;

DROP VIEW IF EXISTS v_app_effect_rows;
CREATE VIEW v_app_effect_rows AS
SELECT
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    effect_kind,
    effect_subkind,
    effect_key,
    effect_name,
    effect_value_text,
    effect_numeric_value,
    effect_package_key,
    COALESCE(NULLIF(trim(spellcasting_name), ''), owner_name) AS spellcasting_name,
    is_prepared,
    MIN(grant_level) AS min_grant_level,
    MAX(grant_level) AS max_grant_level,
    COUNT(*) AS source_row_count,
    COUNT(DISTINCT source_kind) AS source_kind_count,
    GROUP_CONCAT(DISTINCT source_kind) AS source_kinds
FROM v_effect_templates
GROUP BY
    owner_element_id,
    owner_aurora_id,
    owner_name,
    owner_package_key,
    owner_source_path,
    owner_type_name,
    effect_kind,
    effect_subkind,
    effect_key,
    effect_name,
    effect_value_text,
    effect_numeric_value,
    effect_package_key,
    COALESCE(NULLIF(trim(spellcasting_name), ''), owner_name),
    is_prepared;";
            views.ExecuteNonQuery();
        }

        private static void RefreshPrecedenceResolution(SqliteConnection connection, SqliteTransaction transaction)
        {
            RebuildResolvedElementCache(connection, transaction);
            ResolveDeferredRelationships(connection, transaction);
        }

        private static void RefreshPrecedenceResolutionForPackage(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string packageKey)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
            {
                RefreshPrecedenceResolution(connection, transaction);
                return;
            }

            BuildAffectedPrecedenceScope(connection, transaction, packageKey);

            using var affectedCount = connection.CreateCommand();
            affectedCount.Transaction = transaction;
            affectedCount.CommandText = "SELECT COUNT(*) FROM temp.affected_aurora_ids;";
            long affectedAuroraCount = (long)affectedCount.ExecuteScalar();
            if (affectedAuroraCount == 0)
            {
                RefreshPrecedenceResolution(connection, transaction);
                return;
            }

            RebuildResolvedElementCacheForAffectedScope(connection, transaction);
            ResolveDeferredRelationshipsForAffectedScope(connection, transaction);
        }

        private static void EnsureResolutionCachePopulated(SqliteConnection connection)
        {
            using var cacheCount = connection.CreateCommand();
            cacheCount.CommandText = "SELECT COUNT(*) FROM resolved_elements_cache;";
            long resolvedCount = (long)cacheCount.ExecuteScalar();
            if (resolvedCount > 0)
                return;

            using var elementCount = connection.CreateCommand();
            elementCount.CommandText = "SELECT COUNT(*) FROM elements;";
            long elementRowCount = (long)elementCount.ExecuteScalar();
            if (elementRowCount == 0)
                return;

            using var transaction = connection.BeginTransaction();
            RefreshPrecedenceResolution(connection, transaction);
            transaction.Commit();
        }

        private static void RebuildResolvedElementCache(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, "DELETE FROM resolved_unique_element_names_cache;");
            ExecuteSql(connection, transaction, "DELETE FROM resolved_elements_cache;");

            ExecuteSql(connection, transaction, @"
INSERT INTO resolved_elements_cache
(
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
)
WITH ranked AS
(
    SELECT
        e.aurora_id,
        e.element_id AS winning_element_id,
        e.source_file_id,
        sf.content_package_id,
        cp.package_key,
        cp.package_name,
        cp.package_kind,
        cp.precedence_rank,
        COUNT(*) OVER (PARTITION BY e.aurora_id) AS duplicate_count,
        ROW_NUMBER() OVER
        (
            PARTITION BY e.aurora_id
            ORDER BY
                COALESCE(cp.is_enabled, 1) DESC,
                COALESCE(cp.precedence_rank, 500) DESC,
                CASE COALESCE(cp.package_kind, 'local')
                    WHEN 'local' THEN 5
                    WHEN 'homebrew' THEN 4
                    WHEN 'third-party' THEN 3
                    WHEN 'official' THEN 2
                    WHEN 'core' THEN 1
                    ELSE 0
                END DESC,
                e.source_file_id ASC,
                e.element_id ASC
        ) AS resolution_rank
    FROM elements AS e
    JOIN source_files AS sf
        ON sf.source_file_id = e.source_file_id
    LEFT JOIN content_packages AS cp
        ON cp.content_package_id = sf.content_package_id
    WHERE e.aurora_id IS NOT NULL
      AND trim(e.aurora_id) <> ''
      AND COALESCE(cp.is_enabled, 1) = 1
)
SELECT
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
FROM ranked
WHERE resolution_rank = 1;");

            ExecuteSql(connection, transaction, @"
INSERT INTO resolved_unique_element_names_cache
(
    normalized_name,
    winning_element_id,
    name
)
WITH named AS
(
    SELECT
        rec.winning_element_id,
        e.name,
        lower(trim(e.name)) AS normalized_name,
        COUNT(*) OVER (PARTITION BY lower(trim(e.name))) AS match_count
    FROM resolved_elements_cache AS rec
    JOIN elements AS e
        ON e.element_id = rec.winning_element_id
    WHERE e.name IS NOT NULL
      AND trim(e.name) <> ''
)
SELECT
    normalized_name,
    winning_element_id,
    name
FROM named
WHERE match_count = 1;");
        }

        private static void BuildAffectedPrecedenceScope(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string packageKey)
        {
            ExecuteSql(connection, transaction, @"
DROP TABLE IF EXISTS temp.affected_aurora_ids;
DROP TABLE IF EXISTS temp.affected_old_winners;
DROP TABLE IF EXISTS temp.affected_winner_elements;
DROP TABLE IF EXISTS temp.affected_normalized_names;
DROP TABLE IF EXISTS temp.affected_owner_elements;
DROP TABLE IF EXISTS temp.affected_support_tags;
DROP TABLE IF EXISTS temp.affected_selects;

CREATE TEMP TABLE affected_aurora_ids
(
    aurora_id TEXT NOT NULL PRIMARY KEY
);

CREATE TEMP TABLE affected_old_winners
(
    aurora_id TEXT NOT NULL PRIMARY KEY,
    winning_element_id INTEGER NOT NULL
);

CREATE TEMP TABLE affected_winner_elements
(
    element_id INTEGER NOT NULL PRIMARY KEY
);

CREATE TEMP TABLE affected_normalized_names
(
    normalized_name TEXT NOT NULL PRIMARY KEY
);

CREATE TEMP TABLE affected_owner_elements
(
    element_id INTEGER NOT NULL PRIMARY KEY
);

CREATE TEMP TABLE affected_support_tags
(
    support_tag_id INTEGER NOT NULL PRIMARY KEY
);

CREATE TEMP TABLE affected_selects
(
    select_id INTEGER NOT NULL PRIMARY KEY
);");

            using var packageAuroraIds = connection.CreateCommand();
            packageAuroraIds.Transaction = transaction;
            packageAuroraIds.CommandText = @"
INSERT INTO temp.affected_aurora_ids (aurora_id)
SELECT DISTINCT e.aurora_id
FROM elements AS e
JOIN source_files AS sf
    ON sf.source_file_id = e.source_file_id
JOIN content_packages AS cp
    ON cp.content_package_id = sf.content_package_id
WHERE cp.package_key = $package_key
  AND e.aurora_id IS NOT NULL
  AND trim(e.aurora_id) <> '';";
            packageAuroraIds.Parameters.AddWithValue("$package_key", packageKey);
            packageAuroraIds.ExecuteNonQuery();

            ExecuteSql(connection, transaction, @"
INSERT INTO temp.affected_old_winners (aurora_id, winning_element_id)
SELECT rec.aurora_id, rec.winning_element_id
FROM resolved_elements_cache AS rec
JOIN temp.affected_aurora_ids AS ids
    ON ids.aurora_id = rec.aurora_id;");
        }

        private static void RebuildResolvedElementCacheForAffectedScope(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
DELETE FROM resolved_elements_cache
WHERE aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids);");

            ExecuteSql(connection, transaction, @"
INSERT INTO resolved_elements_cache
(
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
)
WITH ranked AS
(
    SELECT
        e.aurora_id,
        e.element_id AS winning_element_id,
        e.source_file_id,
        sf.content_package_id,
        cp.package_key,
        cp.package_name,
        cp.package_kind,
        cp.precedence_rank,
        COUNT(*) OVER (PARTITION BY e.aurora_id) AS duplicate_count,
        ROW_NUMBER() OVER
        (
            PARTITION BY e.aurora_id
            ORDER BY
                COALESCE(cp.is_enabled, 1) DESC,
                COALESCE(cp.precedence_rank, 500) DESC,
                CASE COALESCE(cp.package_kind, 'local')
                    WHEN 'local' THEN 5
                    WHEN 'homebrew' THEN 4
                    WHEN 'third-party' THEN 3
                    WHEN 'official' THEN 2
                    WHEN 'core' THEN 1
                    ELSE 0
                END DESC,
                e.source_file_id ASC,
                e.element_id ASC
        ) AS resolution_rank
    FROM elements AS e
    JOIN temp.affected_aurora_ids AS ids
        ON ids.aurora_id = e.aurora_id
    JOIN source_files AS sf
        ON sf.source_file_id = e.source_file_id
    LEFT JOIN content_packages AS cp
        ON cp.content_package_id = sf.content_package_id
    WHERE COALESCE(cp.is_enabled, 1) = 1
)
SELECT
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
FROM ranked
WHERE resolution_rank = 1;");

            ExecuteSql(connection, transaction, @"
DELETE FROM temp.affected_winner_elements;

INSERT OR IGNORE INTO temp.affected_winner_elements (element_id)
SELECT winning_element_id
FROM temp.affected_old_winners;

INSERT OR IGNORE INTO temp.affected_winner_elements (element_id)
SELECT rec.winning_element_id
FROM resolved_elements_cache AS rec
JOIN temp.affected_aurora_ids AS ids
    ON ids.aurora_id = rec.aurora_id;");

            ExecuteSql(connection, transaction, @"
DELETE FROM temp.affected_normalized_names;

INSERT OR IGNORE INTO temp.affected_normalized_names (normalized_name)
SELECT lower(trim(e.name))
FROM elements AS e
JOIN temp.affected_winner_elements AS winners
    ON winners.element_id = e.element_id
WHERE e.name IS NOT NULL
  AND trim(e.name) <> '';");

            ExecuteSql(connection, transaction, @"
DELETE FROM resolved_unique_element_names_cache
WHERE normalized_name IN (SELECT normalized_name FROM temp.affected_normalized_names);");

            ExecuteSql(connection, transaction, @"
INSERT INTO resolved_unique_element_names_cache
(
    normalized_name,
    winning_element_id,
    name
)
WITH named AS
(
    SELECT
        rec.winning_element_id,
        e.name,
        lower(trim(e.name)) AS normalized_name,
        COUNT(*) OVER (PARTITION BY lower(trim(e.name))) AS match_count
    FROM resolved_elements_cache AS rec
    JOIN elements AS e
        ON e.element_id = rec.winning_element_id
    JOIN temp.affected_normalized_names AS names
        ON names.normalized_name = lower(trim(e.name))
    WHERE e.name IS NOT NULL
      AND trim(e.name) <> ''
)
SELECT
    normalized_name,
    winning_element_id,
    name
FROM named
WHERE match_count = 1;");
        }

        private static void ResolveDeferredRelationshipsForAffectedScope(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
DELETE FROM temp.affected_owner_elements;

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT element_id
FROM features
WHERE parent_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR parent_support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(parent_support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
   OR parent_support_text IN
      (
          SELECT alias_text
          FROM parent_family_aliases
          WHERE link_kind = 'feature-parent'
            AND
            (
                target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
                OR lower(trim(target_name)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
            )
      );

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT f.element_id
FROM features AS f
JOIN elements AS owner
    ON owner.element_id = f.element_id
WHERE f.parent_support_text = 'Background Feature'
  AND owner.source_file_id IN
  (
      SELECT DISTINCT source_file_id
      FROM elements
      WHERE element_id IN (SELECT element_id FROM temp.affected_winner_elements)
  );

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT element_id
FROM subraces
WHERE race_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR parent_support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(parent_support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
   OR lower(trim(parent_support_text)) IN
      (SELECT normalized_name || ' subrace' FROM temp.affected_normalized_names)
   OR lower(trim(parent_support_text)) IN
      (SELECT normalized_name || ' ancestry' FROM temp.affected_normalized_names)
   OR EXISTS
   (
       SELECT 1
       FROM temp.affected_normalized_names AS names
       WHERE lower(trim(subraces.parent_support_text)) LIKE '% ' || names.normalized_name
   );

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT element_id
FROM race_variants
WHERE race_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR parent_support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(parent_support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
   OR lower(trim(parent_support_text)) IN
      (SELECT normalized_name || ' variant' FROM temp.affected_normalized_names)
   OR lower(trim(replace(replace(parent_support_text, 'Variant ', ''), ' Variant', ''))) IN
      (SELECT normalized_name FROM temp.affected_normalized_names);

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT element_id
FROM background_variants
WHERE background_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR parent_support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(parent_support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
   OR lower(trim(parent_support_text)) IN
      (SELECT 'variant ' || normalized_name FROM temp.affected_normalized_names)
   OR lower(trim(replace(parent_support_text, 'Variant ', ''))) IN
      (SELECT normalized_name FROM temp.affected_normalized_names);

INSERT OR IGNORE INTO temp.affected_owner_elements (element_id)
SELECT element_id
FROM archetypes
WHERE parent_class_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR parent_support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(parent_support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
   OR lower(trim(parent_support_text)) IN
      (SELECT normalized_name || ' subclass' FROM temp.affected_normalized_names)
   OR parent_support_text IN
      (
          SELECT alias_text
          FROM parent_family_aliases
          WHERE link_kind = 'archetype-parent'
            AND
            (
                target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
                OR lower(trim(target_name)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
            )
      );");

            ExecuteSql(connection, transaction, @"
UPDATE grants
SET target_element_id = NULL,
    target_semantic_key = NULL,
    target_semantic_kind = NULL,
    target_semantic_name = NULL
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR rule_scope_id IN
      (
          SELECT rs.rule_scope_id
          FROM rule_scopes AS rs
          WHERE rs.owner_element_id IN (SELECT element_id FROM temp.affected_owner_elements)
      );

UPDATE grants
SET target_element_id =
(
    SELECT rec.winning_element_id
    FROM resolved_elements_cache AS rec
    WHERE rec.aurora_id = grants.target_aurora_id
)
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids);");

            ResolveGrantTargets(connection, transaction, affectedScopeOnly: true);

            ExecuteSql(connection, transaction, @"
UPDATE element_extract_items
SET linked_element_id = NULL
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR linked_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR lower(trim(item_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names);

UPDATE element_extract_items
SET linked_element_id =
(
    SELECT COALESCE(
        (
            SELECT rec.winning_element_id
            FROM resolved_elements_cache AS rec
            WHERE rec.aurora_id = element_extract_items.target_aurora_id
        ),
        (
            SELECT runc.winning_element_id
            FROM resolved_unique_element_names_cache AS runc
            WHERE runc.normalized_name = lower(trim(element_extract_items.item_text))
        )
    )
)
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(item_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names);");

            ResolveExtractItemAliases(connection, transaction, affectedScopeOnly: true);

            ExecuteSql(connection, transaction, @"
UPDATE select_items
SET linked_element_id = NULL
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR linked_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR lower(trim(item_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names);

UPDATE select_items
SET linked_element_id =
(
    SELECT COALESCE(
        (
            SELECT rec.winning_element_id
            FROM resolved_elements_cache AS rec
            WHERE rec.aurora_id = select_items.target_aurora_id
        ),
        (
            SELECT runc.winning_element_id
            FROM resolved_unique_element_names_cache AS runc
            WHERE runc.normalized_name = lower(trim(select_items.item_text))
        )
    )
)
WHERE option_kind <> 'text-choice'
  AND
  (
      target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR lower(trim(item_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names)
  );");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id = NULL
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);

UPDATE race_variants
SET race_element_id = NULL
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);

UPDATE background_variants
SET background_element_id = NULL
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);

UPDATE features
SET parent_element_id = NULL
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);

UPDATE archetypes
SET parent_class_element_id = NULL
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
    WHERE parent.aurora_id = subraces.parent_support_text
       OR parent.name = subraces.parent_support_text
       OR subraces.parent_support_text = parent.name || ' Subrace'
       OR subraces.parent_support_text = parent.name || ' Ancestry'
       OR subraces.parent_support_text LIKE '% ' || parent.name
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE race_variants
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
    WHERE parent.aurora_id = race_variants.parent_support_text
       OR parent.name = race_variants.parent_support_text
       OR race_variants.parent_support_text = parent.name || ' Variant'
       OR trim(replace(replace(race_variants.parent_support_text, 'Variant ', ''), ' Variant', '')) = parent.name
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);");

            ExecuteSql(connection, transaction, @"
UPDATE background_variants
SET background_element_id =
(
    SELECT MIN(parent.element_id)
    FROM backgrounds AS b
    JOIN elements AS parent ON parent.element_id = b.element_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
    WHERE parent.aurora_id = background_variants.parent_support_text
       OR parent.name = background_variants.parent_support_text
       OR background_variants.parent_support_text = 'Variant ' || parent.name
       OR trim(replace(background_variants.parent_support_text, 'Variant ', '')) = parent.name
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements);");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT bg.element_id
    FROM elements AS owner
    JOIN backgrounds AS b
        ON 1 = 1
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    WHERE owner.element_id = features.element_id
      AND bg.source_file_id = owner.source_file_id
      AND bg.element_id < owner.element_id
    ORDER BY bg.element_id DESC
    LIMIT 1
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_element_id IS NULL
  AND parent_support_text = 'Background Feature';");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT bg.element_id
    FROM elements AS owner
    JOIN backgrounds AS b
        ON 1 = 1
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    WHERE owner.element_id = features.element_id
      AND bg.source_file_id = owner.source_file_id
    ORDER BY bg.element_id ASC
    LIMIT 1
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_element_id IS NULL
  AND parent_support_text = 'Background Feature';");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT parent.element_id
    FROM parent_family_aliases AS alias
    JOIN elements AS owner
        ON owner.element_id = features.element_id
    LEFT JOIN source_files AS owner_file
        ON owner_file.source_file_id = owner.source_file_id
    JOIN elements AS parent
        ON
        (
            (alias.target_aurora_id IS NOT NULL AND parent.aurora_id = alias.target_aurora_id)
            OR (alias.target_name IS NOT NULL AND parent.name = alias.target_name)
        )
    JOIN element_types AS parent_type
        ON parent_type.element_type_id = parent.element_type_id
    LEFT JOIN source_files AS parent_file
        ON parent_file.source_file_id = parent.source_file_id
    LEFT JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = parent.element_id
    WHERE alias.link_kind = 'feature-parent'
      AND alias.alias_text = features.parent_support_text
      AND (alias.target_type_name IS NULL OR alias.target_type_name = parent_type.type_name)
    ORDER BY
        CASE WHEN owner.source_file_id = parent.source_file_id THEN 1 ELSE 0 END DESC,
        CASE WHEN owner_file.content_package_id = parent_file.content_package_id THEN 1 ELSE 0 END DESC,
        CASE WHEN rec.winning_element_id IS NOT NULL THEN 1 ELSE 0 END DESC,
        COALESCE(rec.precedence_rank, -1) DESC,
        alias.priority ASC,
        parent.element_id ASC
    LIMIT 1
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT parent.element_id
    FROM parent_family_aliases AS alias
    JOIN elements AS owner
        ON owner.element_id = features.element_id
    LEFT JOIN source_files AS owner_file
        ON owner_file.source_file_id = owner.source_file_id
    JOIN elements AS parent
        ON
        (
            (alias.target_aurora_id IS NOT NULL AND parent.aurora_id = alias.target_aurora_id)
            OR (alias.target_name IS NOT NULL AND parent.name = alias.target_name)
        )
    JOIN element_types AS parent_type
        ON parent_type.element_type_id = parent.element_type_id
    LEFT JOIN source_files AS parent_file
        ON parent_file.source_file_id = parent.source_file_id
    LEFT JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = parent.element_id
    WHERE alias.link_kind = 'feature-parent'
      AND alias.alias_text = features.parent_support_text
      AND (alias.target_type_name IS NULL OR alias.target_type_name = parent_type.type_name)
    ORDER BY
        CASE WHEN owner.source_file_id = parent.source_file_id THEN 1 ELSE 0 END DESC,
        CASE WHEN owner_file.content_package_id = parent_file.content_package_id THEN 1 ELSE 0 END DESC,
        CASE WHEN rec.winning_element_id IS NOT NULL THEN 1 ELSE 0 END DESC,
        COALESCE(rec.precedence_rank, -1) DESC,
        alias.priority ASC,
        parent.element_id ASC
    LIMIT 1
)
WHERE parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT MIN(parent.element_id)
    FROM elements AS parent
    JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = parent.element_id
    WHERE parent.aurora_id = features.parent_support_text
       OR parent.name = features.parent_support_text
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT class_element.element_id
    FROM parent_family_aliases AS alias
    JOIN elements AS owner
        ON owner.element_id = archetypes.element_id
    LEFT JOIN source_files AS owner_file
        ON owner_file.source_file_id = owner.source_file_id
    JOIN elements AS class_element
        ON
        (
            (alias.target_aurora_id IS NOT NULL AND class_element.aurora_id = alias.target_aurora_id)
            OR (alias.target_name IS NOT NULL AND class_element.name = alias.target_name)
        )
    JOIN element_types AS et
        ON et.element_type_id = class_element.element_type_id
    LEFT JOIN source_files AS class_file
        ON class_file.source_file_id = class_element.source_file_id
    LEFT JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = class_element.element_id
    WHERE alias.link_kind = 'archetype-parent'
      AND alias.alias_text = archetypes.parent_support_text
      AND (alias.target_type_name IS NULL OR alias.target_type_name = et.type_name)
    ORDER BY
        CASE WHEN owner.source_file_id = class_element.source_file_id THEN 1 ELSE 0 END DESC,
        CASE WHEN owner_file.content_package_id = class_file.content_package_id THEN 1 ELSE 0 END DESC,
        CASE WHEN rec.winning_element_id IS NOT NULL THEN 1 ELSE 0 END DESC,
        COALESCE(rec.precedence_rank, -1) DESC,
        alias.priority ASC,
        class_element.element_id ASC
    LIMIT 1
)
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_class_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS class_element
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = class_element.element_id
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
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_class_element_id IS NULL
  AND parent_support_text IS NOT NULL;

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
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND parent_class_element_id IS NULL;");

            ExecuteSql(connection, transaction, @"
DELETE FROM temp.affected_support_tags;

INSERT OR IGNORE INTO temp.affected_support_tags (support_tag_id)
SELECT support_tag_id
FROM support_tags
WHERE support_text IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR normalized_text IN (SELECT normalized_name FROM temp.affected_normalized_names);");

            ExecuteSql(connection, transaction, @"
DELETE FROM temp.affected_selects;

INSERT OR IGNORE INTO temp.affected_selects (select_id)
SELECT DISTINCT select_id
FROM select_option_links
WHERE option_element_id IN (SELECT element_id FROM temp.affected_winner_elements);

INSERT OR IGNORE INTO temp.affected_selects (select_id)
SELECT DISTINCT ss.select_id
FROM select_supports AS ss
JOIN support_tags AS st
    ON st.support_text = ss.support_text
WHERE st.support_tag_id IN (SELECT support_tag_id FROM temp.affected_support_tags);

INSERT OR IGNORE INTO temp.affected_selects (select_id)
SELECT DISTINCT select_id
FROM select_items
WHERE target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
   OR linked_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR lower(trim(item_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names);");

            ExecuteSql(connection, transaction, @"
DELETE FROM element_support_links
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
   OR linked_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR support_tag_id IN (SELECT support_tag_id FROM temp.affected_support_tags);

DELETE FROM select_support_links
WHERE select_id IN (SELECT select_id FROM temp.affected_selects)
   OR linked_element_id IN (SELECT element_id FROM temp.affected_winner_elements)
   OR support_tag_id IN (SELECT support_tag_id FROM temp.affected_support_tags);

DELETE FROM select_option_links
WHERE select_id IN (SELECT select_id FROM temp.affected_selects)
   OR option_element_id IN (SELECT element_id FROM temp.affected_winner_elements);");

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
        (SELECT rec.winning_element_id FROM resolved_elements_cache AS rec WHERE rec.aurora_id = es.support_text),
        (SELECT runc.winning_element_id FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(es.support_text)))
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM resolved_elements_cache AS rec WHERE rec.aurora_id = es.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(es.support_text))) THEN 'element-name'
        WHEN es.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind,
    0 AS is_primary_parent
FROM element_supports AS es
JOIN support_tags AS st
    ON st.support_text = es.support_text
WHERE es.element_id IN (SELECT element_id FROM temp.affected_owner_elements)
   OR st.support_tag_id IN (SELECT support_tag_id FROM temp.affected_support_tags)
   OR lower(trim(es.support_text)) IN (SELECT normalized_name FROM temp.affected_normalized_names);");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT a.parent_class_element_id
        FROM archetypes AS a
        WHERE a.element_id = element_support_links.element_id
    ),
    resolution_kind = 'archetype-parent',
    is_primary_parent = 1
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM archetypes AS a
      WHERE a.element_id = element_support_links.element_id
        AND a.parent_class_element_id IS NOT NULL
  );

UPDATE element_support_links
SET linked_element_id = (
        SELECT s.race_element_id
        FROM subraces AS s
        WHERE s.element_id = element_support_links.element_id
    ),
    resolution_kind = 'subrace-parent',
    is_primary_parent = 1
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM subraces AS s
      WHERE s.element_id = element_support_links.element_id
        AND s.race_element_id IS NOT NULL
  );

UPDATE element_support_links
SET linked_element_id = (
        SELECT f.parent_element_id
        FROM features AS f
        WHERE f.element_id = element_support_links.element_id
    ),
    resolution_kind = 'feature-parent',
    is_primary_parent = 1
WHERE element_id IN (SELECT element_id FROM temp.affected_owner_elements)
  AND ordinal = 1
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
        (SELECT rec.winning_element_id FROM resolved_elements_cache AS rec WHERE rec.aurora_id = ss.support_text),
        (SELECT runc.winning_element_id FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(ss.support_text)))
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM resolved_elements_cache AS rec WHERE rec.aurora_id = ss.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(ss.support_text))) THEN 'element-name'
        WHEN ss.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind
FROM select_supports AS ss
JOIN support_tags AS st
    ON st.support_text = ss.support_text
WHERE ss.select_id IN (SELECT select_id FROM temp.affected_selects);");

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
    rec.winning_element_id,
    ssl.support_tag_id,
    'support-membership'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN element_supports AS esupport
    ON esupport.support_text = st.support_text
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = esupport.element_id
WHERE ssl.select_id IN (SELECT select_id FROM temp.affected_selects);

INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    rec.winning_element_id,
    ssl.support_tag_id,
    'direct-id'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN resolved_elements_cache AS rec
    ON rec.aurora_id = st.support_text
WHERE ssl.select_id IN (SELECT select_id FROM temp.affected_selects);

INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    runc.winning_element_id,
    ssl.support_tag_id,
    'direct-name'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN resolved_unique_element_names_cache AS runc
    ON runc.normalized_name = lower(trim(st.support_text))
WHERE ssl.select_id IN (SELECT select_id FROM temp.affected_selects);

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
WHERE si.select_id IN (SELECT select_id FROM temp.affected_selects)
  AND si.linked_element_id IS NOT NULL;");
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

        private static Dictionary<string, SourceFileState> LoadExistingSourceFiles(
            SqliteConnection connection, SqliteTransaction transaction)
        {
            var map = new Dictionary<string, SourceFileState>(StringComparer.OrdinalIgnoreCase);
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT source_file_id, relative_path, file_hash, content_package_id FROM source_files;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(1)] = new SourceFileState(
                    reader.GetInt64(0),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3));
            }
            return map;
        }

        private static void DeleteSourceFile(
            SqliteConnection connection, SqliteTransaction transaction, long sourceFileId)
        {
            // ON DELETE CASCADE on elements.source_file_id handles all element child tables.
            // Cross-file nullable FKs (features.parent_element_id, grants.target_element_id, etc.)
            // all have ON DELETE SET NULL, so the DB engine nulls them automatically.
            // Precedence refresh at the end of Import() re-resolves them from text IDs.
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

        private static long EnsureContentPackage(
            SqliteConnection connection,
            SqliteTransaction transaction,
            AuroraFileInfo file)
        {
            ContentPackageDescriptor package = DeriveContentPackage(file);

            ExecuteInsert(connection, transaction,
                @"INSERT OR IGNORE INTO content_packages
(package_key, package_name, package_kind, precedence_rank, is_enabled, package_description, source_url)
VALUES
($package_key, $package_name, $package_kind, $precedence_rank, 1, $package_description, $source_url);",
                ("$package_key", package.PackageKey),
                ("$package_name", package.PackageName),
                ("$package_kind", package.PackageKind),
                ("$precedence_rank", package.PrecedenceRank),
                ("$package_description", (object)package.PackageDescription ?? DBNull.Value),
                ("$source_url", (object)package.SourceUrl ?? DBNull.Value));

            ExecuteInsert(connection, transaction,
                @"UPDATE content_packages
SET
    package_name = $package_name,
    package_kind = $package_kind,
    precedence_rank = $precedence_rank,
    package_description = COALESCE(package_description, $package_description),
    source_url = COALESCE(source_url, $source_url)
WHERE package_key = $package_key;",
                ("$package_key", package.PackageKey),
                ("$package_name", package.PackageName),
                ("$package_kind", package.PackageKind),
                ("$precedence_rank", package.PrecedenceRank),
                ("$package_description", (object)package.PackageDescription ?? DBNull.Value),
                ("$source_url", (object)package.SourceUrl ?? DBNull.Value));

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT content_package_id FROM content_packages WHERE package_key = $package_key;";
            select.Parameters.AddWithValue("$package_key", package.PackageKey);
            return (long)select.ExecuteScalar();
        }

        private static long InsertSourceFile(
            SqliteConnection connection, SqliteTransaction transaction,
            AuroraFileInfo file, long contentPackageId, string hash = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO source_files
(
    relative_path,
    content_package_id,
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
    $content_package_id,
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
            command.Parameters.AddWithValue("$content_package_id",  contentPackageId);
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

        private static void UpdateSourceFileMetadata(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long sourceFileId,
            AuroraFileInfo file,
            long contentPackageId,
            string hash)
        {
            ExecuteInsert(connection, transaction,
                @"UPDATE source_files
SET
    content_package_id = $content_package_id,
    package_name = $package_name,
    package_description = $package_description,
    version_text = $version_text,
    update_file_name = $update_file_name,
    update_url = $update_url,
    author_name = $author_name,
    author_url = $author_url,
    file_hash = $file_hash
WHERE source_file_id = $source_file_id;",
                ("$source_file_id", sourceFileId),
                ("$content_package_id", contentPackageId),
                ("$package_name", (object)file.Name ?? DBNull.Value),
                ("$package_description", (object)file.Description ?? DBNull.Value),
                ("$version_text", (object)file.FileVersion?.versionString ?? DBNull.Value),
                ("$update_file_name", (object)file.FileVersion?.fileName ?? DBNull.Value),
                ("$update_url", (object)file.FileVersion?.fileUrl ?? DBNull.Value),
                ("$author_name", (object)file.Author?.name ?? DBNull.Value),
                ("$author_url", (object)file.Author?.url ?? DBNull.Value),
                ("$file_hash", (object)hash ?? DBNull.Value));
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

            long spellcastingProfileId = GetLastInsertRowId(connection, transaction);
            InsertSpellcastingProfileEntries(connection, transaction, spellcastingProfileId, "list", spellcasting.list?.raw);
            InsertSpellcastingProfileEntries(connection, transaction, spellcastingProfileId, "extend", spellcasting.extendList?.raw);
        }

        private static void InsertSpellcastingProfileEntries(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long spellcastingProfileId,
            string entryKind,
            string rawText)
        {
            IReadOnlyList<string> entries = ParseSpellcastingProfileEntryText(rawText);
            for (int i = 0; i < entries.Count; i++)
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO spellcasting_profile_entries
(spellcasting_profile_id, entry_kind, ordinal, entry_text)
VALUES
($spellcasting_profile_id, $entry_kind, $ordinal, $entry_text);",
                    ("$spellcasting_profile_id", spellcastingProfileId),
                    ("$entry_kind", entryKind),
                    ("$ordinal", i + 1),
                    ("$entry_text", entries[i]));
            }
        }

        private static IReadOnlyList<string> ParseSpellcastingProfileEntryText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return Array.Empty<string>();

            return SplitTopLevel(rawText, ',');
        }

        private static List<string> SplitTopLevel(string input, char separator)
        {
            var values = new List<string>();

            if (string.IsNullOrWhiteSpace(input))
                return values;

            int parenthesesDepth = 0;
            int bracketsDepth = 0;
            int bracesDepth = 0;
            var current = new System.Text.StringBuilder();

            foreach (char ch in input)
            {
                switch (ch)
                {
                    case '(':
                        parenthesesDepth++;
                        break;
                    case ')':
                        parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                        break;
                    case '[':
                        bracketsDepth++;
                        break;
                    case ']':
                        bracketsDepth = Math.Max(0, bracketsDepth - 1);
                        break;
                    case '{':
                        bracesDepth++;
                        break;
                    case '}':
                        bracesDepth = Math.Max(0, bracesDepth - 1);
                        break;
                }

                if (ch == separator
                    && parenthesesDepth == 0
                    && bracketsDepth == 0
                    && bracesDepth == 0)
                {
                    string candidate = current.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                        values.Add(candidate);

                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            string finalCandidate = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(finalCandidate))
                values.Add(finalCandidate);

            return values;
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
                string targetAuroraId = GetGrantTargetAuroraId(grant);

                ExecuteInsert(connection, transaction,
                    @"INSERT INTO grants
(rule_scope_id, ordinal, grant_type, target_aurora_id, name_text, grant_level, spellcasting_name, is_prepared, raw_xml, requirements_text)
VALUES
($rule_scope_id, $ordinal, $grant_type, $target_aurora_id, $name_text, $grant_level, $spellcasting_name, $is_prepared, $raw_xml, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$grant_type", grant.type ?? string.Empty),
                    ("$target_aurora_id", (object)targetAuroraId ?? DBNull.Value),
                    ("$name_text", (object)grant.name ?? DBNull.Value),
                    ("$grant_level", grant.level.HasValue ? grant.level.Value : DBNull.Value),
                    ("$spellcasting_name", (object)grant.spellcasting ?? DBNull.Value),
                    ("$is_prepared", grant.prepared.HasValue ? (grant.prepared.Value ? 1 : 0) : DBNull.Value),
                    ("$raw_xml", (object)grant.rawXml ?? DBNull.Value),
                    ("$requirements_text", (object)grant.requirements?.raw ?? DBNull.Value));
            }

            ordinal = 1;
            foreach (var select in rules.selects ?? Enumerable.Empty<Select>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO selects
(rule_scope_id, ordinal, select_type, name_text, supports_text, select_level, number_to_choose, default_choice_text, is_optional, spellcasting_profile_id, raw_xml, requirements_text)
VALUES
($rule_scope_id, $ordinal, $select_type, $name_text, $supports_text, $select_level, $number_to_choose, $default_choice_text, $is_optional, $spellcasting_profile_id, $raw_xml, $requirements_text);",
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
                    ("$raw_xml", (object)select.rawXml ?? DBNull.Value),
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

                InsertSelectItems(connection, transaction, selectId, select, select.items);
            }

            ordinal = 1;
            foreach (var stat in rules.stats ?? Enumerable.Empty<Stat>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO stats
(rule_scope_id, ordinal, stat_name, value_expression_text, bonus_expression_text, equipped_expression_text, stat_level, inline_display, alt_text, raw_xml, requirements_text)
VALUES
($rule_scope_id, $ordinal, $stat_name, $value_expression_text, $bonus_expression_text, $equipped_expression_text, $stat_level, $inline_display, $alt_text, $raw_xml, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$stat_name", stat.name ?? string.Empty),
                    ("$value_expression_text", (object)stat.value ?? DBNull.Value),
                    ("$bonus_expression_text", (object)stat.bonus ?? DBNull.Value),
                    ("$equipped_expression_text", (object)stat.equipped?.raw ?? DBNull.Value),
                    ("$stat_level", stat.level.HasValue ? stat.level.Value : DBNull.Value),
                    ("$inline_display", stat.inline ? 1 : 0),
                    ("$alt_text", (object)stat.alt ?? DBNull.Value),
                    ("$raw_xml", (object)stat.rawXml ?? DBNull.Value),
                    ("$requirements_text", (object)stat.requirements?.raw ?? DBNull.Value));
            }
        }

        private static void InsertSelectItems(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long selectId,
            Select select,
            IEnumerable<AuroraItemEntry> items)
        {
            if (items?.Any() != true)
                return;

            int ordinal = 1;
            foreach (var item in items)
            {
                string targetAuroraId = GetItemTargetAuroraId(item);
                string optionKind = DetermineSelectItemOptionKind(select, item, targetAuroraId);

                ExecuteInsert(connection, transaction,
                    @"INSERT INTO select_items
(select_id, ordinal, item_text, target_aurora_id, option_kind)
VALUES
($select_id, $ordinal, $item_text, $target_aurora_id, $option_kind);",
                    ("$select_id", selectId),
                    ("$ordinal", ordinal++),
                    ("$item_text", (object)item.value ?? DBNull.Value),
                    ("$target_aurora_id", (object)targetAuroraId ?? DBNull.Value),
                    ("$option_kind", optionKind));

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
            if (LooksLikeAuroraId(attributeId))
            {
                return attributeId;
            }

            if (LooksLikeAuroraId(item?.value))
            {
                return item.value;
            }

            return null;
        }

        private static string GetGrantTargetAuroraId(Grant grant)
        {
            if (LooksLikeAuroraId(grant?.id))
                return grant.id;

            if (LooksLikeAuroraId(grant?.name))
                return grant.name;

            return null;
        }

        private static bool LooksLikeAuroraId(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.TrimStart().StartsWith("ID_", StringComparison.OrdinalIgnoreCase);
        }

        private static string DetermineSelectItemOptionKind(Select select, AuroraItemEntry item, string targetAuroraId)
        {
            if (!string.IsNullOrWhiteSpace(targetAuroraId))
                return "aurora-reference";

            if (string.Equals(select?.type, "List", StringComparison.OrdinalIgnoreCase))
                return "text-choice";

            string itemText = item?.value?.Trim();
            if (IsLikelyTextChoice(select?.name, itemText))
                return "text-choice";

            return "name-reference-candidate";
        }

        private static bool IsLikelyTextChoice(string selectName, string itemText)
        {
            if (string.IsNullOrWhiteSpace(itemText))
                return true;

            string normalizedSelectName = (selectName ?? string.Empty).Trim().ToLowerInvariant();
            string normalizedItemText = itemText.Trim();

            string[] textChoiceKeywords =
            {
                "personality",
                "ideal",
                "bond",
                "flaw",
                "specialty",
                "speciality",
                "trait",
                "harrowing event",
                "memento",
                "life event",
                "favorite scheme",
                "guild business",
                "characteristic"
            };

            if (textChoiceKeywords.Any(keyword => normalizedSelectName.Contains(keyword, StringComparison.Ordinal)))
                return true;

            int wordCount = normalizedItemText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;

            return normalizedItemText.Contains(".")
                || normalizedItemText.Contains(",")
                || normalizedItemText.Contains(";")
                || normalizedItemText.Contains(":")
                || normalizedItemText.Length >= 60
                || wordCount >= 8;
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

        private static ContentPackageDescriptor DeriveContentPackage(AuroraFileInfo file)
        {
            string[] segments = (file.RelativePath ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            string root = segments.Length > 0 ? segments[0].Trim() : "local";
            string child = segments.Length > 1 ? segments[1].Trim() : null;

            string packageKind = root.ToLowerInvariant() switch
            {
                "core" => "core",
                "official" => "official",
                "third-party" => "third-party",
                "thirdparty" => "third-party",
                "supplements" => "homebrew",
                "homebrew" => "homebrew",
                _ => "local"
            };

            int precedenceRank = packageKind switch
            {
                "core" => 100,
                "official" => 200,
                "third-party" => 300,
                "homebrew" => 400,
                _ => 500
            };

            string packageSegment = !string.IsNullOrWhiteSpace(child)
                && (root.Equals("core", StringComparison.OrdinalIgnoreCase)
                    || root.Equals("official", StringComparison.OrdinalIgnoreCase)
                    || root.Equals("third-party", StringComparison.OrdinalIgnoreCase)
                    || root.Equals("thirdparty", StringComparison.OrdinalIgnoreCase)
                    || root.Equals("supplements", StringComparison.OrdinalIgnoreCase)
                    || root.Equals("homebrew", StringComparison.OrdinalIgnoreCase))
                ? child
                : root;

            string packageKey = BuildPackageKey(root, packageSegment);
            string packageName = BuildPackageDisplayName(packageSegment);
            string sourceUrl = file.FileVersion?.fileUrl ?? file.Author?.url;

            return new ContentPackageDescriptor(
                packageKey,
                string.IsNullOrWhiteSpace(packageName) ? "Local Content" : packageName,
                packageKind,
                precedenceRank,
                file.Description,
                sourceUrl);
        }

        private static string BuildPackageKey(string rootSegment, string packageSegment)
        {
            string root = NormalizePackageSegment(rootSegment);
            string package = NormalizePackageSegment(packageSegment);

            return string.IsNullOrWhiteSpace(package)
                ? root
                : $"{root}-{package}";
        }

        private static string NormalizePackageSegment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "local";

            char[] chars = text.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] == '-' || chars[i] == '_'))
                    chars[i] = '-';
            }

            string normalized = new string(chars)
                .Replace("_", "-", StringComparison.Ordinal);

            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

            return normalized.Trim('-');
        }

        private static string BuildPackageDisplayName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string[] words = text
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return string.Join(" ", words.Select(CapitalizeWord));
        }

        private static string CapitalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            return word.Length == 1
                ? word.ToUpperInvariant()
                : char.ToUpperInvariant(word[0]) + word[1..];
        }

        private static void ResolveGrantTargets(
            SqliteConnection connection,
            SqliteTransaction transaction,
            bool affectedScopeOnly)
        {
            string scopeFilter = affectedScopeOnly
                ? @"
  AND
  (
      rs.owner_element_id IN (SELECT element_id FROM temp.affected_owner_elements)
      OR g.target_aurora_id IN (SELECT aurora_id FROM temp.affected_aurora_ids)
  )"
                : string.Empty;

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = $@"
SELECT
    g.grant_id,
    g.grant_type,
    g.target_aurora_id
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
WHERE g.target_element_id IS NULL
  AND COALESCE(g.target_semantic_key, '') = ''
  AND g.target_aurora_id IS NOT NULL{scopeFilter};";

            var grantRows = new List<(long GrantId, string GrantType, string TargetAuroraId)>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    grantRows.Add(
                        (
                            reader.GetInt64(0),
                            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                        ));
                }
            }

            using var updateElement = connection.CreateCommand();
            updateElement.Transaction = transaction;
            updateElement.CommandText = @"
UPDATE grants
SET target_element_id = $target_element_id,
    target_semantic_key = NULL,
    target_semantic_kind = NULL,
    target_semantic_name = NULL
WHERE grant_id = $grant_id;";
            var updateElementId = updateElement.Parameters.Add("$target_element_id", SqliteType.Integer);
            var updateElementGrantId = updateElement.Parameters.Add("$grant_id", SqliteType.Integer);

            using var updateSemantic = connection.CreateCommand();
            updateSemantic.Transaction = transaction;
            updateSemantic.CommandText = @"
UPDATE grants
SET target_semantic_key = $target_semantic_key,
    target_semantic_kind = $target_semantic_kind,
    target_semantic_name = $target_semantic_name
WHERE grant_id = $grant_id;";
            var updateSemanticKey = updateSemantic.Parameters.Add("$target_semantic_key", SqliteType.Text);
            var updateSemanticKind = updateSemantic.Parameters.Add("$target_semantic_kind", SqliteType.Text);
            var updateSemanticName = updateSemantic.Parameters.Add("$target_semantic_name", SqliteType.Text);
            var updateSemanticGrantId = updateSemantic.Parameters.Add("$grant_id", SqliteType.Integer);

            foreach (var grantRow in grantRows)
            {
                if (TryResolveGrantElementFallback(connection, transaction, grantRow.GrantType, grantRow.TargetAuroraId, out long targetElementId))
                {
                    updateElementId.Value = targetElementId;
                    updateElementGrantId.Value = grantRow.GrantId;
                    updateElement.ExecuteNonQuery();
                    continue;
                }

                if (TryResolveGrantSemantic(grantRow.TargetAuroraId, out string semanticKey, out string semanticKind, out string semanticName))
                {
                    updateSemanticKey.Value = semanticKey;
                    updateSemanticKind.Value = semanticKind;
                    updateSemanticName.Value = semanticName;
                    updateSemanticGrantId.Value = grantRow.GrantId;
                    updateSemantic.ExecuteNonQuery();
                }
            }
        }

        private static bool TryResolveGrantElementFallback(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string grantType,
            string targetAuroraId,
            out long elementId)
        {
            elementId = 0;
            if (string.IsNullOrWhiteSpace(targetAuroraId))
                return false;

            foreach (var alias in BuildGrantTargetAliases(grantType, targetAuroraId))
            {
                if (TryResolveElementByResolvedName(connection, transaction, alias.TargetName, alias.TypeNames, out elementId))
                    return true;
            }

            foreach (var candidateName in BuildGrantTargetCandidateNames(grantType, targetAuroraId))
            {
                if (TryResolveElementByResolvedName(connection, transaction, candidateName, GetGrantFallbackTypeNames(grantType), out elementId))
                    return true;
            }

            return false;
        }

        private static bool TryResolveGrantSemantic(
            string targetAuroraId,
            out string semanticKey,
            out string semanticKind,
            out string semanticName)
        {
            semanticKey = null;
            semanticKind = null;
            semanticName = null;

            if (string.IsNullOrWhiteSpace(targetAuroraId))
                return false;

            if (targetAuroraId.StartsWith("ID_SIZE_", StringComparison.OrdinalIgnoreCase))
            {
                string sizeName = HumanizeAuroraToken(targetAuroraId["ID_SIZE_".Length..]);
                semanticKey = targetAuroraId;
                semanticKind = "size";
                semanticName = sizeName;
                return true;
            }

            if (!targetAuroraId.StartsWith("ID_INTERNAL_", StringComparison.OrdinalIgnoreCase))
                return false;

            string internalToken = targetAuroraId["ID_INTERNAL_".Length..];
            semanticKey = targetAuroraId;

            if (internalToken.StartsWith("CONDITION_DAMAGE_RESISTANCE_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "damage-resistance";
                semanticName = HumanizeAuroraToken(internalToken["CONDITION_DAMAGE_RESISTANCE_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("CONDITION_DAMAGE_IMMUNITY_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "damage-immunity";
                semanticName = HumanizeAuroraToken(internalToken["CONDITION_DAMAGE_IMMUNITY_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("CONDITION_DAMAGE_VULNERABILITY_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "damage-vulnerability";
                semanticName = HumanizeAuroraToken(internalToken["CONDITION_DAMAGE_VULNERABILITY_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("CONDITION_CONDITION_IMMUNITY_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "condition-immunity";
                semanticName = HumanizeAuroraToken(internalToken["CONDITION_CONDITION_IMMUNITY_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("GRANT_MULTICLASS_SPELLCASTING_SLOTS_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "multiclass-spellcasting-slots";
                semanticName = HumanizeAuroraToken(internalToken["GRANT_MULTICLASS_SPELLCASTING_SLOTS_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("GRANTS_MULTICLASS_SPELLCASTING_SLOTS_", StringComparison.OrdinalIgnoreCase))
            {
                semanticKind = "multiclass-spellcasting-slots";
                semanticName = HumanizeAuroraToken(internalToken["GRANTS_MULTICLASS_SPELLCASTING_SLOTS_".Length..]);
                return true;
            }

            if (internalToken.StartsWith("GRANT_", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = internalToken["GRANT_".Length..];
                semanticKind = BuildSemanticKindFromInternalGrantSuffix(suffix);
                semanticName = HumanizeAuroraToken(suffix);
                return true;
            }

            if (internalToken.StartsWith("GRANTS_", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = internalToken["GRANTS_".Length..];
                semanticKind = BuildSemanticKindFromInternalGrantSuffix(suffix);
                semanticName = HumanizeAuroraToken(suffix);
                return true;
            }

            semanticKind = internalToken.ToLowerInvariant().Replace('_', '-');
            semanticName = HumanizeAuroraToken(internalToken);
            return true;
        }

        private static void ResolveExtractItemAliases(
            SqliteConnection connection,
            SqliteTransaction transaction,
            bool affectedScopeOnly)
        {
            string scopeFilter = affectedScopeOnly
                ? @"
  AND ex.element_id IN (SELECT element_id FROM temp.affected_owner_elements)"
                : string.Empty;

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = $@"
SELECT
    ei.extract_item_id,
    ei.target_aurora_id
FROM element_extract_items AS ei
JOIN element_extracts AS ex
    ON ex.element_id = ei.element_id
WHERE ei.linked_element_id IS NULL
  AND ei.target_aurora_id IS NOT NULL{scopeFilter};";

            var rows = new List<(long ExtractItemId, string TargetAuroraId)>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
                }
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
UPDATE element_extract_items
SET linked_element_id = $linked_element_id
WHERE extract_item_id = $extract_item_id;";
            var linkedElementId = update.Parameters.Add("$linked_element_id", SqliteType.Integer);
            var extractItemId = update.Parameters.Add("$extract_item_id", SqliteType.Integer);

            foreach (var row in rows)
            {
                if (!ExtractTargetAliasMap.TryGetValue(row.TargetAuroraId ?? string.Empty, out var alias))
                    continue;

                if (!TryResolveElementByResolvedName(connection, transaction, alias.TargetName, alias.TypeNames, out long resolvedElementId))
                    continue;

                linkedElementId.Value = resolvedElementId;
                extractItemId.Value = row.ExtractItemId;
                update.ExecuteNonQuery();
            }
        }

        private static string BuildSemanticKindFromInternalGrantSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                return "internal-grant";

            if (suffix.StartsWith("MULTICLASS_SPELLCASTING_SLOTS_", StringComparison.OrdinalIgnoreCase))
                return "multiclass-spellcasting-slots";

            return suffix.ToLowerInvariant().Replace('_', '-');
        }

        private static bool TryResolveElementByResolvedName(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string targetName,
            IReadOnlyList<string> typeNames,
            out long elementId)
        {
            elementId = 0;
            if (string.IsNullOrWhiteSpace(targetName) || typeNames == null || typeNames.Count == 0)
                return false;

            string normalizedName = targetName.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedName))
                return false;

            foreach (string typeName in typeNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
SELECT rec.winning_element_id
FROM resolved_elements_cache AS rec
JOIN elements AS e
    ON e.element_id = rec.winning_element_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
WHERE lower(trim(e.name)) = $normalized_name
  AND et.type_name = $type_name
ORDER BY rec.precedence_rank DESC, rec.winning_element_id ASC
LIMIT 1;";
                command.Parameters.AddWithValue("$normalized_name", normalizedName);
                command.Parameters.AddWithValue("$type_name", typeName);
                object result = command.ExecuteScalar();
                if (result is long longId)
                {
                    elementId = longId;
                    return true;
                }

                if (result is int intId)
                {
                    elementId = intId;
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> GetGrantFallbackTypeNames(string grantType)
        {
            string normalizedGrantType = grantType?.Trim() ?? string.Empty;
            return normalizedGrantType switch
            {
                "Feat Feature" => new[] { "Feat Feature", "Feat" },
                "Class Feature" => new[] { "Class Feature" },
                "Archetype Feature" => new[] { "Archetype Feature" },
                "Language" => new[] { "Language" },
                "Spell" => new[] { "Spell" },
                _ => string.IsNullOrWhiteSpace(normalizedGrantType)
                    ? Array.Empty<string>()
                    : new[] { normalizedGrantType }
            };
        }

        private static IEnumerable<(string TargetName, IReadOnlyList<string> TypeNames)> BuildGrantTargetAliases(string grantType, string targetAuroraId)
        {
            if (string.IsNullOrWhiteSpace(targetAuroraId))
                yield break;

            if (GrantTargetAliasMap.TryGetValue(targetAuroraId, out var alias))
            {
                yield return alias;
            }
        }

        private static IEnumerable<string> BuildGrantTargetCandidateNames(string grantType, string targetAuroraId)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(targetAuroraId))
                return candidates;

            string[] tokens = targetAuroraId
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !token.Equals("ID", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (tokens.Length == 0)
                return candidates;

            void AddCandidate(IEnumerable<string> candidateTokens)
            {
                string candidate = HumanizeAuroraToken(candidateTokens);
                if (!string.IsNullOrWhiteSpace(candidate))
                    candidates.Add(candidate);
            }

            int spellIndex = Array.LastIndexOf(tokens, "SPELL");
            if (spellIndex >= 0 && spellIndex < tokens.Length - 1)
                AddCandidate(tokens.Skip(spellIndex + 1));

            int languageIndex = Array.LastIndexOf(tokens, "LANGUAGE");
            if (languageIndex >= 0 && languageIndex < tokens.Length - 1)
                AddCandidate(tokens.Skip(languageIndex + 1));

            int featuresIndex = Array.LastIndexOf(tokens, "FEATURES");
            if (featuresIndex > 0)
            {
                int startIndex = Array.FindLastIndex(tokens, featuresIndex - 1,
                    token => token is "FEAT" or "CLASS" or "ARCHETYPE" or "RACIAL" or "RACE");
                startIndex = startIndex >= 0 ? startIndex + 1 : 1;
                if (startIndex < featuresIndex)
                    AddCandidate(tokens.Skip(startIndex).Take(featuresIndex - startIndex));
            }

            int featureIndex = Array.LastIndexOf(tokens, "FEATURE");
            if (featureIndex >= 0 && featureIndex < tokens.Length - 1)
            {
                string[] trailing = tokens.Skip(featureIndex + 1)
                    .SkipWhile(token => token is "REPLACEMENT" or "OPTION" or "OPTIONS")
                    .ToArray();
                if (trailing.Length > 0)
                {
                    AddCandidate(trailing);
                    if (trailing.Length > 1)
                    {
                        string candidate = $"{HumanizeAuroraToken(trailing.Skip(1))}: {HumanizeAuroraToken(trailing.Take(1))}";
                        if (!string.IsNullOrWhiteSpace(candidate))
                            candidates.Add(candidate);
                    }
                }
            }

            return candidates;
        }

        private static string HumanizeAuroraToken(IEnumerable<string> tokens)
        {
            if (tokens == null)
                return null;

            string[] words = tokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(CapitalizeWord)
                .ToArray();

            return words.Length == 0 ? null : string.Join(" ", words);
        }

        private static string HumanizeAuroraToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            return HumanizeAuroraToken(token.Split('_', StringSplitOptions.RemoveEmptyEntries));
        }

        private static void ResolveDeferredRelationships(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
UPDATE grants
SET target_element_id = NULL,
    target_semantic_key = NULL,
    target_semantic_kind = NULL,
    target_semantic_name = NULL
WHERE target_aurora_id IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE element_extract_items
SET linked_element_id = NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE select_items
SET linked_element_id = NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id = NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE race_variants
SET race_element_id = NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE background_variants
SET background_element_id = NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id = NULL
WHERE parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id = NULL;");

            ExecuteSql(connection, transaction, "DELETE FROM select_option_links;");
            ExecuteSql(connection, transaction, "DELETE FROM select_support_links;");
            ExecuteSql(connection, transaction, "DELETE FROM element_support_links;");

            ExecuteSql(connection, transaction, @"
DELETE FROM support_tags
WHERE support_text <> '[[inline-item]]'
  AND NOT EXISTS (SELECT 1 FROM element_supports WHERE element_supports.support_text = support_tags.support_text)
  AND NOT EXISTS (SELECT 1 FROM select_supports WHERE select_supports.support_text = support_tags.support_text);");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'unclassified'
WHERE support_text <> '[[inline-item]]';");

            ExecuteSql(connection, transaction, @"
UPDATE grants
SET target_element_id =
(
    SELECT rec.winning_element_id
    FROM resolved_elements_cache AS rec
    WHERE rec.aurora_id = grants.target_aurora_id
)
WHERE target_element_id IS NULL
  AND target_aurora_id IS NOT NULL;");

            ResolveGrantTargets(connection, transaction, affectedScopeOnly: false);

            ExecuteSql(connection, transaction, @"
UPDATE element_extract_items
SET linked_element_id =
(
    SELECT COALESCE(
        (
            SELECT rec.winning_element_id
            FROM resolved_elements_cache AS rec
            WHERE rec.aurora_id = element_extract_items.target_aurora_id
        ),
        (
            SELECT runc.winning_element_id
            FROM resolved_unique_element_names_cache AS runc
            WHERE runc.normalized_name = lower(trim(element_extract_items.item_text))
        )
    )
)
WHERE linked_element_id IS NULL
  AND (target_aurora_id IS NOT NULL OR item_text IS NOT NULL);");

            ResolveExtractItemAliases(connection, transaction, affectedScopeOnly: false);

            ExecuteSql(connection, transaction, @"
UPDATE select_items
SET linked_element_id =
(
    SELECT COALESCE(
        (
            SELECT rec.winning_element_id
            FROM resolved_elements_cache AS rec
            WHERE rec.aurora_id = select_items.target_aurora_id
        ),
        (
            SELECT runc.winning_element_id
            FROM resolved_unique_element_names_cache AS runc
            WHERE runc.normalized_name = lower(trim(select_items.item_text))
        )
    )
)
WHERE option_kind <> 'text-choice'
  AND linked_element_id IS NULL
  AND (target_aurora_id IS NOT NULL OR item_text IS NOT NULL);");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
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
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
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
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = parent.element_id
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
    SELECT bg.element_id
    FROM elements AS owner
    JOIN backgrounds AS b
        ON 1 = 1
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    WHERE owner.element_id = features.element_id
      AND bg.source_file_id = owner.source_file_id
      AND bg.element_id < owner.element_id
    ORDER BY bg.element_id DESC
    LIMIT 1
)
WHERE parent_element_id IS NULL
  AND parent_support_text = 'Background Feature';");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT bg.element_id
    FROM elements AS owner
    JOIN backgrounds AS b
        ON 1 = 1
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    WHERE owner.element_id = features.element_id
      AND bg.source_file_id = owner.source_file_id
    ORDER BY bg.element_id ASC
    LIMIT 1
)
WHERE parent_element_id IS NULL
  AND parent_support_text = 'Background Feature';");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT parent.element_id
    FROM parent_family_aliases AS alias
    JOIN elements AS owner
        ON owner.element_id = features.element_id
    LEFT JOIN source_files AS owner_file
        ON owner_file.source_file_id = owner.source_file_id
    JOIN elements AS parent
        ON
        (
            (alias.target_aurora_id IS NOT NULL AND parent.aurora_id = alias.target_aurora_id)
            OR (alias.target_name IS NOT NULL AND parent.name = alias.target_name)
        )
    JOIN element_types AS parent_type
        ON parent_type.element_type_id = parent.element_type_id
    LEFT JOIN source_files AS parent_file
        ON parent_file.source_file_id = parent.source_file_id
    LEFT JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = parent.element_id
    WHERE alias.link_kind = 'feature-parent'
      AND alias.alias_text = features.parent_support_text
      AND (alias.target_type_name IS NULL OR alias.target_type_name = parent_type.type_name)
    ORDER BY
        CASE WHEN owner.source_file_id = parent.source_file_id THEN 1 ELSE 0 END DESC,
        CASE WHEN owner_file.content_package_id = parent_file.content_package_id THEN 1 ELSE 0 END DESC,
        CASE WHEN rec.winning_element_id IS NOT NULL THEN 1 ELSE 0 END DESC,
        COALESCE(rec.precedence_rank, -1) DESC,
        alias.priority ASC,
        parent.element_id ASC
    LIMIT 1
)
WHERE parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT MIN(parent.element_id)
    FROM elements AS parent
    JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = parent.element_id
    WHERE parent.aurora_id = features.parent_support_text
       OR parent.name = features.parent_support_text
)
WHERE parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT class_element.element_id
    FROM parent_family_aliases AS alias
    JOIN elements AS owner
        ON owner.element_id = archetypes.element_id
    LEFT JOIN source_files AS owner_file
        ON owner_file.source_file_id = owner.source_file_id
    JOIN elements AS class_element
        ON
        (
            (alias.target_aurora_id IS NOT NULL AND class_element.aurora_id = alias.target_aurora_id)
            OR (alias.target_name IS NOT NULL AND class_element.name = alias.target_name)
        )
    JOIN element_types AS et
        ON et.element_type_id = class_element.element_type_id
    LEFT JOIN source_files AS class_file
        ON class_file.source_file_id = class_element.source_file_id
    LEFT JOIN resolved_elements_cache AS rec
        ON rec.winning_element_id = class_element.element_id
    WHERE alias.link_kind = 'archetype-parent'
      AND alias.alias_text = archetypes.parent_support_text
      AND (alias.target_type_name IS NULL OR alias.target_type_name = et.type_name)
    ORDER BY
        CASE WHEN owner.source_file_id = class_element.source_file_id THEN 1 ELSE 0 END DESC,
        CASE WHEN owner_file.content_package_id = class_file.content_package_id THEN 1 ELSE 0 END DESC,
        CASE WHEN rec.winning_element_id IS NOT NULL THEN 1 ELSE 0 END DESC,
        COALESCE(rec.precedence_rank, -1) DESC,
        alias.priority ASC,
        class_element.element_id ASC
    LIMIT 1
)
WHERE parent_class_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS class_element
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    JOIN resolved_elements_cache AS rec ON rec.winning_element_id = class_element.element_id
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
        (SELECT rec.winning_element_id FROM resolved_elements_cache AS rec WHERE rec.aurora_id = es.support_text),
        (SELECT runc.winning_element_id FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(es.support_text)))
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM resolved_elements_cache AS rec WHERE rec.aurora_id = es.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(es.support_text))) THEN 'element-name'
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
        (SELECT rec.winning_element_id FROM resolved_elements_cache AS rec WHERE rec.aurora_id = ss.support_text),
        (SELECT runc.winning_element_id FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(ss.support_text)))
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM resolved_elements_cache AS rec WHERE rec.aurora_id = ss.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM resolved_unique_element_names_cache AS runc WHERE runc.normalized_name = lower(trim(ss.support_text))) THEN 'element-name'
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
    rec.winning_element_id,
    ssl.support_tag_id,
    'support-membership'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN element_supports AS esupport
    ON esupport.support_text = st.support_text
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = esupport.element_id;");

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
    rec.winning_element_id,
    ssl.support_tag_id,
    'direct-id'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN resolved_elements_cache AS rec
    ON rec.aurora_id = st.support_text;");

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
    runc.winning_element_id,
    ssl.support_tag_id,
    'direct-name'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN resolved_unique_element_names_cache AS runc
    ON runc.normalized_name = lower(trim(st.support_text));");

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
        private sealed record SourceFileState(long Id, string Hash, long? ContentPackageId);
        private sealed record ContentPackageDescriptor(
            string PackageKey,
            string PackageName,
            string PackageKind,
            int PrecedenceRank,
            string PackageDescription,
            string SourceUrl);
        internal sealed record ContentPackageInfo(
            string PackageKey,
            string PackageName,
            string PackageKind,
            int PrecedenceRank,
            bool IsEnabled,
            int FileCount,
            int WinningElementCount,
            int DuplicateElementCount);
        internal sealed record UnresolvedLinkPatternSummary(
            string UnresolvedKey,
            string UnresolvedText,
            string DisplayKey,
            string DisplayText,
            int Count,
            IReadOnlyList<string> SampleOwners);
        internal sealed record UnresolvedLinkDeferredSummary(
            string DiagnosticStatus,
            string DiagnosticReason,
            string LinkKind,
            int Count);
        internal sealed record UnresolvedLinkKindSummary(
            string LinkKind,
            int TotalCount,
            IReadOnlyList<UnresolvedLinkPatternSummary> Patterns);
        internal sealed record UnresolvedLinkDiagnosticsReport(
            long TotalUnresolvedCount,
            long ActionableUnresolvedCount,
            IReadOnlyList<UnresolvedLinkDeferredSummary> DeferredSummaries,
            IReadOnlyList<UnresolvedLinkKindSummary> KindSummaries);
        internal sealed record SourceIntegrityPatternSummary(
            string IssueKey,
            string IssueText,
            string DisplayKey,
            string DisplayText,
            int Count,
            IReadOnlyList<string> SampleRows);
        internal sealed record SourceIntegrityKindSummary(
            string IssueKind,
            int TotalCount,
            IReadOnlyList<SourceIntegrityPatternSummary> Patterns);
        internal sealed record SourceIntegrityDiagnosticsReport(
            int TotalIssueCount,
            IReadOnlyList<SourceIntegrityKindSummary> KindSummaries);
        internal sealed record PackageRefreshParityTableResult(
            string TableName,
            long ScopedRowCount,
            long FullRowCount,
            long ScopedOnlyRowCount,
            long FullOnlyRowCount)
        {
            public bool IsMatch => ScopedOnlyRowCount == 0 && FullOnlyRowCount == 0;
        }
        internal sealed record PackageRefreshParityResult(
            string PackageKey,
            int? RequestedPrecedenceRank,
            bool? RequestedIsEnabled,
            IReadOnlyList<PackageRefreshParityTableResult> TableResults)
        {
            public bool IsMatch => TableResults.All(x => x.IsMatch);
        }

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

        private static void InsertSrdCreature(SqliteConnection connection, SqliteTransaction transaction, AuroraTranslator.Models.SrdMonster m)
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
