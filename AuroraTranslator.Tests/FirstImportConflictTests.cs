using Aurora.Content.Contracts;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

internal static class FirstImportConflictTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Element(string id, string description = "same") =>
        $"<element id='{id}' name='Example' type='Class Feature' source='Test'><description>{description}</description></element>";
    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal void Write(string relative, params string[] elements)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<elements>" + string.Concat(elements) + "</elements>");
        }
        internal ContentImportResult Import(bool skip = false) =>
            ContentImport.ImportAsync(Root, Database, skipUnusableContent: skip).GetAwaiter().GetResult();
        internal SqliteConnection Open()
        {
            var c = new SqliteConnection($"Data Source={Database};Pooling=False");
            c.Open(); return c;
        }
        internal long Count(string sql)
        {
            using var c = Open(); using var q = c.CreateCommand(); q.CommandText = sql;
            return Convert.ToInt64(q.ExecuteScalar());
        }
        internal string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Database)));
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    internal static void ConflictsExcludeOnlyAffectedIds()
    {
        foreach (bool reverse in new[] { false, true })
        {
            using var w = new Workspace();
            w.Write("core/a.xml", Element("ID_CONFLICT", reverse ? "second" : "first"), Element("ID_GOOD_A"), Element("ID_IDENTICAL"));
            w.Write("core/b.xml", Element("ID_CONFLICT", reverse ? "first" : "second"), Element("ID_GOOD_B"), Element("ID_IDENTICAL"));
            w.Write("core/c.xml", Element("ID_CONFLICT", "third"), Element("ID_GOOD_C"));
            var inputHashes = Directory.GetFiles(w.Root, "*.xml", SearchOption.AllDirectories)
                .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
            var result = w.Import();
            Require(result.Skipped.Count == 1 && result.Skipped[0].Kind == "definition-conflict", "A conflict is reported once per ID, independently of skip-unusable permission.");
            Require(new[] { "a.xml", "b.xml", "c.xml" }.All(p => result.Skipped[0].Detail.Contains(p)), "Every supplier is named for review.");
            Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_CONFLICT'") == 0, "No path-order winner may enter the database.");
            Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id IN ('ID_GOOD_A','ID_GOOD_B','ID_GOOD_C','ID_IDENTICAL')") == 4, "Unrelated definitions and identical duplicates remain available.");
            Require(w.Count("SELECT COUNT(*) FROM content_declaration_provenance WHERE aurora_id='ID_CONFLICT'") == 3, "All conflicted supplier definitions remain inspectable in provenance.");
            Require(ContentDatabaseReader.ReadUnavailableIds(w.Database).SetEquals(["ID_CONFLICT"]), "Unavailable IDs survive database reopening.");
            Require(inputHashes.All(p => p.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))), "Import must not edit authoritative XML.");
        }
    }

    internal static void SameFileAndSpellingConflictsAreUnavailable()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_SAME", "one"), Element("ID_SAME", "two"),
            Element(" ID_SPELL "), Element("id_spell"), Element("ID_GOOD"));
        w.Import();
        Require(w.Count("SELECT COUNT(*) FROM elements") == 1, "All same-file and spelling-conflicting definitions must be excluded, retaining the unrelated element.");
        var ids = ContentDatabaseReader.ReadUnavailableIds(w.Database);
        Require(ids.SetEquals(["ID_SAME", " ID_SPELL ", "ID_SPELL", "id_spell"]), "Raw IDs and the consumer's trimmed identity must be unavailable.");
    }

    internal static void ExistingDatabaseIsPreservedOnNewConflict()
    {
        foreach (bool skip in new[] { false, true })
        {
            using var w = new Workspace();
            w.Write("core/a.xml", Element("ID_EXISTING"));
            w.Import(); string before = w.Hash();
            w.Write("core/b.xml", Element("ID_EXISTING", "conflicting update"), Element("ID_UNRELATED_UPDATE"));
            bool refused = false;
            try { w.Import(skip); }
            catch (InvalidDataException ex) { refused = ex.Message.Contains("existing database was preserved"); }
            Require(refused && w.Hash() == before, "A new conflict must stop activation and preserve the exact existing database, even when skipping unreadable files is enabled.");
        }
    }

    internal static void UnavailableIdsPersistUntilRepair()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_CONFLICT", "one"), Element("ID_GOOD"));
        w.Write("core/b.xml", Element("ID_CONFLICT", "two"));
        w.Import();
        w.Write("core/c.xml", Element("ID_NEW"));
        w.Import();
        Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_NEW'") == 1 &&
            ContentDatabaseReader.ReadUnavailableIds(w.Database).Contains("ID_CONFLICT"), "An existing unavailable identity must not block unrelated later imports.");
        w.Write("core/b.xml", Element("ID_CONFLICT", "one"));
        w.Import();
        Require(ContentDatabaseReader.ReadUnavailableIds(w.Database).Count == 0 &&
            ContentDatabaseReader.ReadSkippedContent(w.Database).Count == 0 &&
            w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_CONFLICT'") == 1, "Repair and refresh must restore the definition and clear both reports.");
    }

    internal static void RuntimeAndHostDefinitionsCannotResurrectConflicts()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_CONFLICT", "one"), Element("ID_GOOD"));
        w.Write("user/homebrew.xml", Element("ID_CONFLICT", "two"), Element("ID_LOCAL_GOOD"));
        w.Write("core/appends.xml", "<append id='ID_CONFLICT'><supports>Forbidden</supports></append>",
            "<append id='ID_GOOD'><supports>Allowed</supports></append>");
        w.Import();
        using var c = w.Open();
        var runtime = RuntimeContentFiles.Read(c, w.Root, []).ToList();
        Require(runtime.Any(f => f.Source.RelativePath.Contains("homebrew.xml")), "A conflict must not discard unrelated runtime content in the same file.");
        var secondary = new PreparedCatalogSource(Path.Combine(w.Root, "secondary.xml"), "secondary.xml", "test", "user");
        runtime.Add(new(secondary, "<elements>" + Element("ID_CONFLICT", "runtime") + Element("ID_SECONDARY_GOOD") +
            "<append id='ID_CONFLICT'><supports>Runtime forbidden</supports></append></elements>"));
        var host = new PreparedCatalogElement("ID_CONFLICT", secondary, Element("ID_CONFLICT", "host"));
        var projection = PreparedCatalogReader.Read(c, hostDefinitions: [host], runtimeFiles: runtime);
        Require(!projection.Elements.Any(e => e.AuroraId == "ID_CONFLICT"), "Database, runtime and host catalogs must agree on unavailability.");
        Require(new[] { "ID_GOOD", "ID_LOCAL_GOOD", "ID_SECONDARY_GOOD" }.All(id => projection.Elements.Any(e => e.AuroraId == id)), "Good database and runtime definitions must remain available.");
        Require(projection.Elements.Single(e => e.AuroraId == "ID_GOOD").Xml.Contains("Allowed"), "Unaffected append operations still apply.");
        Require(w.Count("SELECT COUNT(*) FROM content_append_operations WHERE target_aurora_id='ID_CONFLICT' AND status='unavailable-target'") == 1, "An append to an unavailable definition stays inspectable but unapplied.");
    }

    internal static void UnreadableSupplierCannotPromoteAlternative()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_CONFLICT", "one"), Element("ID_GOOD"));
        w.Write("core/b.xml", Element("ID_CONFLICT", "two"));
        w.Import();
        string before = w.Hash();
        File.WriteAllText(Path.Combine(w.Root, "core/b.xml"), "<elements><element");
        bool refused = false;
        try { w.Import(skip: true); }
        catch (InvalidDataException ex)
        {
            refused = ex.Message.Contains("existing conflict") && ex.Message.Contains("core") &&
                ex.Message.Contains("existing database was preserved");
        }
        Require(refused && w.Hash() == before, "Skipping an unreadable old conflict supplier must not activate its alternative or change the working database.");
        Require(ContentDatabaseReader.ReadUnavailableIds(w.Database).Contains("ID_CONFLICT") &&
            w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_CONFLICT'") == 0,
            "The identity must remain unavailable until its former supplier is repaired or deliberately removed.");
    }

    internal static void CaseVariantsCannotResurrectConflicts()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_SPELL", "one"), Element("ID_GOOD"));
        w.Write("core/b.xml", Element("id_spell", "two"));
        w.Write("core/append.xml", "<append id='iD_SpElL'><supports>Forbidden</supports></append>");
        w.Import();
        Require(w.Count("SELECT COUNT(*) FROM content_append_operations WHERE status='unavailable-target'") == 1,
            "A differently cased append target must retain the same conflict availability status.");
        using var c = w.Open();
        var source = new PreparedCatalogSource(Path.Combine(w.Root, "runtime.xml"), "runtime.xml", "test", "user");
        var runtime = new PreparedCatalogFile(source, "<elements>" + Element(" Id_SpElL ", "runtime") +
            "<append id='iD_sPeLl'><supports>Forbidden</supports></append></elements>");
        var host = new PreparedCatalogElement("iD_sPeLl", source, Element("iD_sPeLl", "host"));
        var projection = PreparedCatalogReader.Read(c, hostDefinitions: [host], runtimeFiles: [runtime]);
        Require(projection.Elements.Count == 1 && projection.Elements[0].AuroraId == "ID_GOOD" &&
            projection.UnresolvedAppends.Count == 0,
            "Trimmed case variants from runtime, host and append inputs must not resurrect a quarantined identity.");
    }

    internal static void ProtectedCorrectionConflictStopsFirstActivation()
    {
        using var w = new Workspace();
        string baseline = "<elements>" + Element("ID_CORRECTION", "original") + "</elements>";
        w.Write("core/a.xml", Element("ID_CORRECTION", "original"));
        w.Write("core/b.xml", Element("ID_CORRECTION", "unexplained"));
        string local = Path.Combine(w.Root, "user/local/fix.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, LocalCorrectionDocument.Create(baseline.Replace("original", "protected"), baseline, "core/a.xml",
            [new("fix", "replace", "ID_CORRECTION", null, null, "review-pending", null)]));
        bool refused = false;
        try { w.Import(skip: true); }
        catch (InvalidDataException ex) { refused = ex.Message.Contains("explicit correction targets"); }
        Require(refused && !File.Exists(w.Database) && File.Exists(local), "Protected corrections cannot be silently quarantined or retired, even on first installation.");
    }
}
