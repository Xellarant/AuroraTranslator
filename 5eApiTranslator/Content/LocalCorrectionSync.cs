#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AuroraTranslator.Content.ContentPreparation;
using Builder.Data.Files;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace AuroraTranslator.Content;

internal sealed record CorrectionImportResult(bool Success);

public sealed record LocalCorrectionStatus(string FilePath, string SourcePath, string Status, string ReviewDetails);
public sealed record LocalCorrectionRuntimeContent(string EffectiveXml, IReadOnlyList<string> SuppressedIds, string SourcePath);

/// <summary>Stages effective content without rewriting authoritative or local XML.</summary>
internal static class LocalCorrectionSync
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static async Task<CorrectionImportResult> ImportAsync(IReadOnlyList<string> roots, string database,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<CorrectionImportResult>> import,
        CancellationToken cancellationToken = default)
    {
        using var prepared = ContentPreparation.Prepare(roots, cancellationToken, ReadDisabledPackages(database));
        var files = prepared.Files;
        var managed = prepared.Managed;
        string candidate = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, ".aurora-candidate-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
            if (File.Exists(database))
            {
                using var source = Open(database);
                using var destination = Open(candidate);
                source.BackupDatabase(destination);
            }
            var result = await import(prepared.StagedRoots, candidate, cancellationToken);
            if (!result.Success) return result;
            cancellationToken.ThrowIfCancellationRequested();
            using (var connection = Open(candidate))
            {
                Execute(connection, "PRAGMA foreign_keys=ON;");
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Candidate database failed integrity validation.");
                check.CommandText = "PRAGMA foreign_key_check;";
                using (var reader = check.ExecuteReader())
                    if (reader.Read()) throw new InvalidDataException("Candidate database contains broken foreign keys.");
                PreparedContentWriter.ValidateDatabase(connection, prepared);
                Mirror(connection, managed);
                PreparedContentWriter.WriteProvenance(connection, prepared);
                // Track the real inputs separately. Source-file hashes continue to describe
                // staged effective content, so the importer correctly detects the next change.
                Execute(connection, "CREATE TABLE IF NOT EXISTS local_correction_inputs (path TEXT PRIMARY KEY, sha256 TEXT NOT NULL); DELETE FROM local_correction_inputs;");
                foreach (var file in files)
                    Execute(connection, "INSERT INTO local_correction_inputs VALUES ($path,$hash);", ("$path", Path.GetFullPath(file.Path)), ("$hash", file.Hash));
                using var remaining = connection.CreateCommand();
                remaining.CommandText = "SELECT COUNT(*) FROM local_override_files";
                if (Convert.ToInt64(remaining.ExecuteScalar()) == 0)
                    Execute(connection, "DROP TABLE local_correction_inputs;");
            }
            // Refuse to activate a candidate built from files edited during the import.
            var currentPaths = roots.SelectMany(root => Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!currentPaths.SetEquals(files.Select(f => Path.GetFullPath(f.Path))) || files.Any(f => !File.Exists(f.Path) || Hash(f.Path) != f.Hash))
                throw new IOException("Content changed during sync; the working database was preserved. Retry sync.");
            cancellationToken.ThrowIfCancellationRequested();
            // All readers in this module use nonpooled connections; release writer handles too.
            SqliteConnection.ClearAllPools();
            File.Move(candidate, database, overwrite: true);
            foreach (var entry in managed.Where(m => m.Evaluation.CanRetire))
            {
                // Retain the complete XML as a recoverable, non-scanned artifact. If an
                // editor holds the file open, retry retirement on the next successful sync.
                try
                {
                    if (File.Exists(entry.File.Path) && Hash(entry.File.Path) == entry.File.Hash)
                    {
                        File.Move(entry.File.Path, entry.File.Path + ".retired-" + Guid.NewGuid().ToString("N"));
                        using var connection = Open(database);
                        Execute(connection, "UPDATE local_override_files SET status='retired' WHERE file_path=$path; DELETE FROM local_correction_inputs WHERE path=$path;", ("$path", Path.GetFullPath(entry.File.Path)));
                    }
                }
                catch (IOException) { /* Safe to leave a redundant file active until next sync. */ }
                catch (UnauthorizedAccessException) { }
                catch (SqliteException) { /* Retirement is recoverable; the database is already active. */ }
            }
            return result;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static IReadOnlySet<string> ReadDisabledPackages(string database)
    {
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(database)) return disabled;
        using var connection = Open(database);
        if (!HasTable(connection, "content_packages")) return disabled;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT package_key FROM content_packages WHERE is_enabled=0";
        using var reader = command.ExecuteReader();
        while (reader.Read()) disabled.Add(reader.GetString(0));
        return disabled;
    }

    public static bool? IsStale(IReadOnlyList<string> roots, string database)
    {
        if (!File.Exists(database)) return null;
        using var connection = Open(database);
        if (!HasTable(connection, "local_correction_inputs")) return null;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,sha256 FROM local_correction_inputs";
        var recorded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) recorded.Add(reader.GetString(0), reader.GetString(1));
        var paths = roots.SelectMany(r => Directory.EnumerateFiles(r, "*.xml", SearchOption.AllDirectories)).Select(Path.GetFullPath).ToList();
        return paths.Count != recorded.Count || paths.Any(p => !recorded.TryGetValue(p, out string? hash) || hash != Hash(p));
    }

    public static IReadOnlyList<LocalCorrectionStatus> ReadStatuses(string database)
    {
        if (!File.Exists(database)) return [];
        using var connection = Open(database);
        if (!HasTable(connection, "local_override_files")) return [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_path,source_path,status,review_details FROM local_override_files ORDER BY file_path";
        var result = new List<LocalCorrectionStatus>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            string.Join("; ", JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [])));
        return result;
    }

    public static LocalCorrectionRuntimeContent? ReadRuntimeContent(string filePath, string database)
    {
        if (!File.Exists(database)) return null;
        string? root = LocalCorrectionDocument.FindContentRoot(filePath);
        if (root == null) return null;
        using var connection = Open(database);
        if (!HasTable(connection, "local_override_files")) return null;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_path,effective_xml,suppressed_ids FROM local_override_files WHERE file_path=$path AND status <> 'retired'";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(filePath));
        string source, xml, suppressed;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            source = LocalCorrectionDocument.ResolveSourcePath(root, reader.GetString(0));
            xml = reader.GetString(1);
            suppressed = reader.GetString(2);
        }
        foreach (string path in new[] { Path.GetFullPath(filePath), source })
        {
            command.CommandText = "SELECT sha256 FROM local_correction_inputs WHERE path=$path";
            command.Parameters["$path"].Value = path;
            if (!File.Exists(path) || (string?)command.ExecuteScalar() != Hash(path)) return null;
        }
        return new(xml, JsonSerializer.Deserialize<string[]>(suppressed) ?? [], source);
    }

    private static void Mirror(SqliteConnection connection, List<ManagedFile> managed)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS local_override_files (
              file_path TEXT PRIMARY KEY, source_path TEXT NOT NULL, local_xml TEXT NOT NULL,
              baseline_xml TEXT NOT NULL, upstream_xml TEXT NOT NULL, effective_xml TEXT NOT NULL,
              status TEXT NOT NULL, review_details TEXT NOT NULL, suppressed_ids TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS local_corrections (
              file_path TEXT NOT NULL REFERENCES local_override_files(file_path) ON DELETE CASCADE,
              correction_key TEXT NOT NULL, operation TEXT NOT NULL, target_id TEXT NOT NULL,
              replacement_id TEXT, original_fingerprint TEXT, state TEXT NOT NULL,
              review_group TEXT, reason TEXT, PRIMARY KEY(file_path,correction_key));
            DELETE FROM local_corrections WHERE file_path IN (SELECT file_path FROM local_override_files WHERE status <> 'retired');
            DELETE FROM local_override_files WHERE status <> 'retired';
            """, transaction: transaction);
        foreach (var entry in managed)
        {
            var e = entry.Evaluation;
            string path = Path.GetFullPath(entry.File.Path);
            Execute(connection, "INSERT OR REPLACE INTO local_override_files VALUES ($path,$source,$local,$baseline,$upstream,$effective,$status,$reviews,$suppressed)", transaction,
                ("$path", path), ("$source", e.SourcePath), ("$local", e.LocalXml), ("$baseline", e.BaselineXml),
                ("$upstream", e.UpstreamXml), ("$effective", e.EffectiveXml),
                ("$status", e.CanRetire ? "ready-to-retire" : "review-required"), ("$reviews", JsonSerializer.Serialize(e.ReviewReasons)),
                ("$suppressed", JsonSerializer.Serialize(e.SuppressedIds)));
            foreach (var c in e.Corrections)
                Execute(connection, "INSERT INTO local_corrections VALUES ($path,$key,$operation,$target,$replacement,$fingerprint,$state,$group,$reason)", transaction,
                    ("$path", path), ("$key", c.Key), ("$operation", c.Operation), ("$target", c.TargetId),
                    ("$replacement", c.ReplacementId), ("$fingerprint", c.OriginalFingerprint), ("$state", c.State), ("$group", c.Group), ("$reason", c.Reason));
        }
        transaction.Commit();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }
    private static void Execute(SqliteConnection c, string sql, params (string, object?)[] values) => Execute(c, sql, null, values);
    private static void Execute(SqliteConnection c, string sql, SqliteTransaction? transaction, params (string, object?)[] values)
    {
        using var command = c.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
