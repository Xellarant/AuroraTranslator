using System.Xml.Linq;
using Aurora.Content.Contracts;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

internal static class SkippedAppendProjectionTests
{
    private const string Base = "<elements><element id='ID_BASE' name='Base' type='Proficiency' source='Test'/></elements>";
    private const string Extra = "<element id='ID_EXTRA' name='Extra' type='Proficiency' source='Test'/>";
    private const string GoodAppend = "<append id='ID_BASE'><supports>valid-extension</supports></append>";
    private const string BadAppend = "<append id='ID_BASE' type='Race'><description>wrong type</description></append>";

    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal Workspace() => Write("core/base.xml", Base);
        internal void Write(string relative, string xml)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        internal ContentImportResult Import() => ContentImport.ImportAsync(Root, Database,
            skipUnusableContent: true).GetAwaiter().GetResult();
        internal PreparedCatalogProjection Read(bool runtime = false, bool includePrimary = false)
        {
            using var connection = ContentDatabase.OpenReadableConnection(Database);
            var files = runtime ? RuntimeContentFiles.Read(connection, Root, [], includePrimary) : null;
            return PreparedCatalogReader.Read(connection, runtimeFiles: files);
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void RequireGoodContent(PreparedCatalogProjection projection)
    {
        Require(projection.Elements.Any(e => e.AuroraId == "ID_EXTRA"), "An append skip must retain the file's elements.");
        var element = XElement.Parse(projection.Elements.Single(e => e.AuroraId == "ID_BASE").Xml);
        Require(element.Element("supports")?.Value == "valid-extension", "Valid append operations must still apply exactly once.");
        Require(projection.UnresolvedAppends.Count == 0, "Rejected operations must not return as unresolved runtime operations.");
    }

    internal static void StoredOperationsStaySkipped()
    {
        using var w = new Workspace();
        w.Write("core/appends.xml", "<elements>" + Extra + BadAppend + GoodAppend + "</elements>");
        Require(w.Import().Skipped.Count == 1, "The conflicting append must be reported once.");
        RequireGoodContent(w.Read());
        RequireGoodContent(w.Read(runtime: true, includePrimary: true));
    }

    internal static void RuntimeMalformedOperationStaysSkipped()
    {
        using var w = new Workspace();
        w.Write("user/appends.xml", "<elements>" + Extra + "<append><description>missing id</description></append>" + GoodAppend + "</elements>");
        Require(w.Import().Skipped.Count == 1, "The malformed append must be reported once.");
        RequireGoodContent(w.Read(runtime: true));
    }

    internal static void RepairedOperationIsReadImmediately()
    {
        using var w = new Workspace();
        w.Write("user/appends.xml", "<elements>" + Extra + BadAppend + "</elements>");
        w.Import();
        w.Write("user/appends.xml", "<elements>" + Extra + GoodAppend + "</elements>");
        RequireGoodContent(w.Read(runtime: true));
        Require(ContentDatabaseReader.IsStale([w.Root], w.Database), "The changed input must still request a refresh.");
        Require(w.Import().Skipped.Count == 0, "The repaired operation must leave the next skip report.");
        RequireGoodContent(w.Read());
    }

    internal static void ChangedRevisionIsNotSilentlySkipped()
    {
        using var w = new Workspace();
        w.Write("user/appends.xml", "<elements>" + Extra + BadAppend + "</elements>");
        w.Import();
        w.Write("user/appends.xml", "<elements><!-- another revision -->" + Extra + BadAppend + "</elements>");
        try { w.Read(runtime: true); }
        catch (InvalidDataException error) when (error.Message.Contains("Append type does not match")) { return; }
        throw new Exception("A rejection applies only to the recorded input revision; changed content must be evaluated again.");
    }

    internal static void RepairedWholeFileIsReadImmediately()
    {
        using var w = new Workspace();
        w.Write("user/appends.xml", "<elements><element");
        Require(w.Import().Skipped.Count == 1, "The malformed file must be skipped.");
        w.Write("user/appends.xml", "<elements>" + Extra + GoodAppend + "</elements>");
        RequireGoodContent(w.Read(runtime: true));
    }

    internal static void CorrectedSourceKeepsRejectedAppendSkipped()
    {
        using var w = new Workspace();
        string baseline = Base.Replace("</elements>", Extra + BadAppend + GoodAppend + "</elements>");
        w.Write("core/base.xml", baseline);
        w.Write("user/local/fix.xml", LocalCorrectionDocument.Create(
            baseline.Replace("name='Base'", "name='Corrected'"), baseline, "core/base.xml",
            [new("fix-name", "replace", "ID_BASE", null, null)]));
        Require(w.Import().Skipped.Count == 1, "Only the invalid append must be skipped.");
        var projection = w.Read(runtime: true);
        RequireGoodContent(projection);
        Require(XElement.Parse(projection.Elements.Single(e => e.AuroraId == "ID_BASE").Xml).Attribute("name")?.Value == "Corrected",
            "Filtering a rejected append must retain the protected local replacement.");
    }
}
