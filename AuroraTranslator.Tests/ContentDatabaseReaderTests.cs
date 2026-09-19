using Microsoft.Data.Sqlite;

internal static class ContentDatabaseReaderTests
{
    private const string Features = "<elements><element id='ID_READER_FEATURE' name='Feature' type='Class Feature' source='Test'><description>text</description></element></elements>";
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal Workspace() { Write("core/features.xml", Features); }
        internal void Write(string relative, string xml) { string path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, xml); }
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
        internal bool IsStale() => ContentDatabaseReader.IsStale([Root], Database);
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    internal static void Staleness()
    {
        using var w = new Workspace();
        Require(w.IsStale(), "A missing database must need an import");
        w.Import();
        Require(!w.IsStale(), "A fresh import must be current");
        w.Write("core/features.xml", Features.Replace(">text<", ">changed<"));
        Require(w.IsStale(), "An edited input must need an import");
        w.Import();
        w.Write("user/extra.xml", "<elements />");
        Require(w.IsStale(), "An added input must need an import");
        w.Import();
        File.Delete(Path.Combine(w.Root, "user", "extra.xml"));
        Require(w.IsStale(), "A removed input must need an import");
    }

    internal static void UnpreparedDatabasesAreStale()
    {
        using var w = new Workspace();
        w.Import();
        using (var connection = new SqliteConnection($"Data Source={w.Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE database_metadata SET data_version = data_version - 1";
            command.ExecuteNonQuery();
        }
        Require(w.IsStale(), "An older data version must need an import");

        using (var connection = new SqliteConnection($"Data Source={w.Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE database_metadata SET data_version = data_version + 1; DROP TABLE content_preparation_metadata;";
            command.ExecuteNonQuery();
        }
        Require(w.IsStale(), "A database without the preparation contract must need an import");
    }

    internal static void MetadataAndHealth()
    {
        using var w = new Workspace();
        Require(ContentDatabaseReader.ReadMetadata(w.Database) == null, "A missing database has no metadata");
        Require(ContentDatabaseReader.ReadHealth(w.Database) == null, "A missing database has no health report");
        w.Import();
        var metadata = ContentDatabaseReader.ReadMetadata(w.Database) ?? throw new Exception("Metadata missing");
        Require(metadata.DataVersion == AuroraSqliteImporter.CurrentDataVersion, $"Data version {metadata.DataVersion}");
        Require(metadata.ElementCount > 0 && metadata.SourceFileCount == 1, $"Counts {metadata.ElementCount}/{metadata.SourceFileCount}");
        var health = ContentDatabaseReader.ReadHealth(w.Database) ?? throw new Exception("Health missing");
        Require(health.Status == ContentDatabaseHealthStatus.Healthy && health.BlockingIssueCount == 0,
            $"Health {health.Status}, {health.BlockingIssueCount} blocking");
        Require(ContentDatabaseReader.ReadLocalCorrections(w.Database).Count == 0, "No corrections expected");
    }
}
