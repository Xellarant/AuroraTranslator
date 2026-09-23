using Microsoft.Data.Sqlite;

/// <summary>
/// Content is written by hand, so a reference may carry stray whitespace and a file may have a root
/// this importer cannot read. Neither should cost a character its content silently.
/// </summary>
internal static class ContentReferenceHygieneTests
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
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
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

    private const string Target = "<element name='Target' type='Proficiency' source='Test' id='ID_TARGET' />";

    internal static void PaddedReferencesStillResolve()
    {
        foreach (string attribute in new[] { "id", "name" })
        {
            using var w = new Workspace();
            w.Write("core/content.xml", "<elements>" + Target +
                "<element name='Owner' type='Background' source='Test' id='ID_OWNER'><rules>" +
                $"<grant type='Proficiency' {attribute}='  ID_TARGET  '/>" +
                "</rules></element></elements>");
            w.Import();

            Require(w.Scalar("SELECT COUNT(*) FROM grants WHERE target_element_id IS NOT NULL") == 1,
                $"A grant whose {attribute} carries whitespace must still reach its target.");
            Require(w.Scalar("SELECT COUNT(*) FROM grants WHERE target_aurora_id = 'ID_TARGET'") == 1,
                "The reference is stored without the padding.");
        }
    }

    internal static void UnreadableRootNamesTheFile()
    {
        using var w = new Workspace();
        w.Write("core/broken.xml", "<elements xmlns='http://example.com/schema'>" + Target + "</elements>");

        try
        {
            w.Import();
        }
        catch (InvalidDataException error)
        {
            Require(error.Message.Contains("broken.xml", StringComparison.OrdinalIgnoreCase),
                "The error must name the file to fix: " + error.Message);
            Require(error.Message.Contains("unnamespaced elements root", StringComparison.OrdinalIgnoreCase),
                "The error must say what is wrong with it: " + error.Message);
            return;
        }
        throw new Exception("A content file whose root cannot be read must not import silently.");
    }
}
