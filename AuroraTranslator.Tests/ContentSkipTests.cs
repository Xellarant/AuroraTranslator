using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

/// <summary>
/// One bad file in a collection should not cost the user every other file. When the caller allows
/// it, a file the import cannot use is left out and the reason is kept in the database for the user
/// to act on; without that permission the import still refuses, so a silent success never hides a
/// file that was dropped.
/// </summary>
internal static class ContentSkipTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;

        internal void Write(string relative, string xml)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }

        internal ContentImportResult Import(bool skip) =>
            ContentImport.ImportAsync(Root, Database, skipUnusableContent: skip).GetAwaiter().GetResult();

        internal long Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    private const string Good =
        "<elements><element name='Good' type='Proficiency' source='Test' id='ID_GOOD' /></elements>";

    internal static void AnUnreadableFileIsSkippedAndReported()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Good);
        w.Write("core/broken.xml", "<elements xmlns='http://example.com/schema'><element name='Lost' type='Proficiency' source='Test' id='ID_LOST' /></elements>");

        var result = w.Import(skip: true);

        Require(result.Skipped.Count == 1, $"Exactly the one unusable file is skipped, not {result.Skipped.Count}.");
        var skipped = result.Skipped[0];
        Require(skipped.Kind == "unreadable", "A file whose root cannot be read is reported as unreadable, not " + skipped.Kind);
        Require(skipped.RelativePath.Contains("broken.xml", StringComparison.OrdinalIgnoreCase),
            "The skip names the file to fix: " + skipped.RelativePath);
        Require(skipped.Detail.Contains("unnamespaced elements root", StringComparison.OrdinalIgnoreCase),
            "The skip says what is wrong with it: " + skipped.Detail);

        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_GOOD'") == 1,
            "The files that could be read are still imported.");
        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_LOST'") == 0,
            "Nothing from a skipped file reaches the catalog.");

        var recorded = ContentDatabaseReader.ReadSkippedContent(w.Database);
        Require(recorded.Count == 1 && recorded[0].Kind == "unreadable",
            "The skip is kept in the database so the user can still see it later.");
        Require(recorded[0].Path.Contains("broken.xml", StringComparison.OrdinalIgnoreCase),
            "The stored skip names the file: " + recorded[0].Path);
    }

    internal static void ConflictingDefinitionsLeaveOtherElementsAvailable()
    {
        using var w = new Workspace();
        w.Write("core/a-first.xml",
            "<elements><element name='Shared' type='Proficiency' source='Test' id='ID_SHARED'><description>first</description></element></elements>");
        w.Write("core/b-second.xml",
            "<elements><element name='Shared' type='Proficiency' source='Test' id='ID_SHARED'><description>second</description></element>" +
            "<element name='Other' type='Proficiency' source='Test' id='ID_OTHER' /></elements>");

        var result = w.Import(skip: true);

        Require(result.Skipped.Count == 1, $"One conflicted ID, not {result.Skipped.Count}.");
        var skipped = result.Skipped[0];
        Require(skipped.Kind == "definition-collision", "A redefinition is reported as a skipped collision, not " + skipped.Kind);
        Require(skipped.Detail.Contains("a-first.xml") && skipped.Detail.Contains("b-second.xml"),
            "The report names both suppliers for comparison.");

        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_SHARED' AND declaration_status='effective'") == 1,
            "Skip mode makes a provisional definition available on first installation.");
        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_OTHER'") == 1,
            "Unrelated definitions in the same file remain available.");
    }

    internal static void WithoutPermissionTheImportStillRefuses()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Good);
        w.Write("core/broken.xml", "<elements xmlns='http://example.com/schema'><element name='Lost' type='Proficiency' source='Test' id='ID_LOST' /></elements>");

        try
        {
            w.Import(skip: false);
        }
        catch (InvalidDataException)
        {
            Require(!File.Exists(w.Database), "A refused import leaves no database behind it.");
            return;
        }
        throw new Exception("Skipping is opt-in: an import that was not allowed to skip must refuse the whole content set.");
    }

    internal static void FixingTheFileClearsTheReport()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Good);
        w.Write("core/broken.xml", "<elements xmlns='http://example.com/schema'><element name='Later' type='Proficiency' source='Test' id='ID_LATER' /></elements>");
        w.Import(skip: true);
        Require(ContentDatabaseReader.ReadSkippedContent(w.Database).Count == 1, "The first import reports the bad file.");

        // The file on disk is never touched by the import, so the user can still repair it.
        w.Write("core/broken.xml", "<elements><element name='Later' type='Proficiency' source='Test' id='ID_LATER' /></elements>");
        w.Import(skip: true);

        Require(ContentDatabaseReader.ReadSkippedContent(w.Database).Count == 0,
            "A file that has been fixed stops being reported.");
        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_LATER'") == 1,
            "and its content is imported.");
    }

    internal static void SkippedFilesStayOutOfTheRuntimeRead()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Good);
        // Under user/, where the runtime overlay reads the files on disk rather than the database.
        w.Write("user/homebrew.xml", "<elements><element name='Broken' type='Proficiency' source='Test' id='ID_BROKEN'>");

        w.Import(skip: true);

        using var connection = new SqliteConnection($"Data Source={w.Database};Pooling=False");
        connection.Open();
        var runtime = RuntimeContentFiles.Read(connection, w.Root, []);

        Require(!runtime.Any(f => f.Source.FilePath.Contains("homebrew.xml", StringComparison.OrdinalIgnoreCase)),
            "A file the import skipped must not be read back at load time; reading it would fail for the same reason.");
    }

    internal static void AnUnusableAppendLeavesTheRestOfItsFile()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Good);
        w.Write("core/extra.xml",
            "<elements><element name='Extra' type='Proficiency' source='Test' id='ID_EXTRA' />" +
            "<append><description>no target</description></append></elements>");

        var result = w.Import(skip: true);

        Require(result.Skipped.Count == 1 && result.Skipped[0].Kind == "append",
            "An append with no id is reported against its file as an append skip.");
        Require(w.Scalar("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_EXTRA'") == 1,
            "The elements the file declares are still imported; only the operation is dropped.");
    }
}
