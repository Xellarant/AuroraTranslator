using Aurora.Content.Contracts;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;

internal static class ForgivingImportTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Element(string id, string name, string body = "") => $"<element id='{id}' name='{name}' type='Class Feature' source='Test'>{body}</element>";
    private static string Origin(string owner) => $"<info><update version='1.0'><file url='https://raw.githubusercontent.com/{owner}/elements/master/features.xml'/></update></info>";
    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal string PathFor(string relative) => Path.Combine(Root, relative);
        internal void Write(string relative, string content, bool raw = false)
        {
            string path = PathFor(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, raw ? content : "<elements>" + content + "</elements>");
        }
        internal ContentImportResult Import(bool skip = true, IProgress<ContentImportProgress>? progress = null) =>
            ContentImport.ImportAsync(Root, Database, progress, skipUnusableContent: skip).GetAwaiter().GetResult();
        internal SqliteConnection Open() { var c = new SqliteConnection($"Data Source={Database};Pooling=False"); c.Open(); return c; }
        internal string Query(string sql) { using var c = Open(); using var q = c.CreateCommand(); q.CommandText = sql; return Convert.ToString(q.ExecuteScalar()) ?? ""; }
        internal string Project(string id, bool runtime = false)
        {
            using var c = Open();
            var files = runtime ? RuntimeContentFiles.Read(c, Root, [], includePrimary: true) : [];
            return PreparedCatalogReader.Read(c, runtimeFiles: files).Elements.Single(e => e.AuroraId == id).Xml;
        }
        internal string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Database)));
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }
    private static void Refuses(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException)
        { Require(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), ex.ToString()); return; }
        throw new Exception("Expected refusal: " + message);
    }

    internal static void TypedFailures()
    {
        foreach (string bad in new[] {
            Element("ID_BAD", "Bad", "<sheet display='perhaps'/>"),
            "<element id='ID_BAD' type='Class Feature' source='Test'/>",
            Element("ID_BAD", "Bad", "<rules><select type='Feat' name='Bad' number='many'/></rules>"),
            Element("ID_BAD", "Bad", "<rules><grant type='Feat' id='ID_OTHER' level='99999999999999999'/></rules>") })
        {
            using var w = new Workspace();
            w.Write("core/good.xml", Element("ID_GOOD", "Good")); w.Write("core/bad.xml", bad);
            Refuses(() => w.Import(false), "bad.xml");
            Require(!File.Exists(w.Database), "A failed strict import must not activate a database.");
            var imported = w.Import();
            Require(imported.Skipped.Any(s => s.Kind == "unreadable" && s.Detail.Contains("ID_BAD") && s.Detail.Contains("bad.xml")), "Report the original file and declaration identity.");
            Require(w.Query("SELECT COUNT(*) FROM elements") == "1" && w.Project("ID_GOOD", true).Contains("Good"), "Other files survive typed failures.");
            w.Write("core/bad.xml", Element("ID_BAD", "Working")); w.Import();
            w.Write("core/bad.xml", bad); w.Import(); w.Import();
            Require(w.Project("ID_BAD", true).Contains("Working"), "A broken typed revision retains its previous working content across imports.");
        }
    }

    internal static void SpellDescriptionAndAppend()
    {
        using var w = new Workspace();
        w.Write("core/spell.xml", "<element id='ID_SPELL' name='Spell' type='Spell' source='Test'><description>At Higher Levels. Extra damage.</description></element>");
        w.Write("core/good.xml", Element("ID_GOOD", "Good"));
        w.Write("core/append.xml", "<append id='ID_GOOD'><rules><select type='Feat' name='Bad' number='many'/></rules></append>" +
            "<append id='ID_GOOD'><supports>WorkingAppend</supports></append>");
        var result = w.Import();
        Require(w.Project("ID_SPELL").Contains("At Higher Levels."), "A description beginning with the heading is usable.");
        Require(result.Skipped.Single().Kind == "append" && w.Project("ID_GOOD", true).Contains("WorkingAppend"), "Validate typed append effects before replacing the target; retain valid companion operations.");
        Require(!w.Project("ID_GOOD", true).Contains("many"), "The invalid append must also be excluded at runtime.");
    }

    internal static void UnreadableIndependentSupplier()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_SHARED", "Independent")); w.Import();
        w.Write("core/b.xml", Origin("aurorabuilder") + Element("ID_SHARED", "Archived"));
        w.Write("core/c.xml", Origin("AuroraLegacy") + Element("ID_SHARED", "Legacy")); w.Import();
        w.Query("UPDATE database_metadata SET data_version=14; ALTER TABLE content_definition_suppliers RENAME TO old_suppliers; " +
            "CREATE TABLE content_definition_suppliers (aurora_id TEXT NOT NULL,file_path TEXT NOT NULL,PRIMARY KEY(aurora_id,file_path)); " +
            "INSERT INTO content_definition_suppliers SELECT aurora_id,file_path FROM old_suppliers; DROP TABLE old_suppliers;");
        w.Write("core/a.xml", "<elements><broken", raw: true);
        w.Write("core/c.xml", Origin("AuroraLegacy") + Element("ID_SHARED", "LegacyUpdated"));
        w.Import(); w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Independent") && w.Query("SELECT kind FROM content_definition_resolutions") == "retained", "An unreadable independent competitor cannot authorize succession.");
        Require(w.Query("SELECT data_version FROM database_metadata") == ContentDatabaseReader.CurrentDataVersion.ToString(), "Migrate data-14 supplier evidence before applying the current policy.");
        File.Delete(w.PathFor("core/a.xml")); w.Import();
        Require(w.Project("ID_SHARED", true).Contains("LegacyUpdated"), "Deliberately removing that competitor permits known succession.");
    }

    internal static void UnreadableAppendSupplier()
    {
        using var w = new Workspace();
        w.Write("core/base.xml", Element("ID_BASE", "Base"));
        const string extension = "<append id='ID_BASE'><supports>WorkingExtension</supports><rules><stat name='test:extension' value='3'/></rules></append>";
        w.Write("core/append.xml", extension); w.Import();
        w.Write("core/append.xml", "<elements><broken", raw: true);
        w.Write("core/base.xml", Element("ID_BASE", "BaseEdited") + Element("ID_NEW", "New"));
        w.Import(); w.Import();
        foreach (bool runtime in new[] { false, true })
            Require(w.Project("ID_BASE", runtime).Contains("WorkingExtension") && !w.Project("ID_BASE", runtime).Contains("BaseEdited"), "Retain the effective target, not just its base, across unreadable append refreshes.");
        Require(w.Project("ID_NEW").Contains("New"), "Unrelated additions still import.");
        Require(w.Query("SELECT supplier_kind FROM content_definition_suppliers WHERE file_path LIKE '%append.xml'") == "append", "Remember append dependencies distinctly from definition suppliers.");
        w.Write("core/append.xml", extension); w.Import();
        Require(w.Project("ID_BASE", true).Contains("BaseEdited") && w.Query("SELECT COUNT(*) FROM stats WHERE stat_name='test:extension'") == "1", "Repair releases retention and applies the extension once.");
        w.Write("core/append.xml", "<elements><broken", raw: true); w.Import();
        File.Delete(w.PathFor("core/base.xml")); w.Import();
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_BASE'") == "0", "An unreadable append cannot resurrect an intentionally removed base definition.");
    }

    internal static void LockedInputs()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Element("ID_GOOD", "Good"));
        w.Write("user/local/unknown.xml", Element("ID_UNKNOWN", "Unknown"));
        using (var locked = File.Open(w.PathFor("user/local/unknown.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = w.Import();
            Require(result.Skipped.Single().Kind == "unreadable" && w.Query("SELECT COUNT(*) FROM elements") == "1", "Unknown inaccessible files are skipped as explicitly approved.");
            Require(!ContentDatabaseReader.IsStale([w.Root], w.Database), "A still-unreadable recorded input does not cause endless refresh.");
            Require(w.Project("ID_GOOD", true).Contains("Good"), "The runtime reader honors the capture failure.");
        }
        Require(ContentDatabaseReader.IsStale([w.Root], w.Database), "An accessible repair requires a refresh.");
        w.Import();
        using (var locked = File.Open(w.PathFor("core/good.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            w.Import(); w.Import();
            Require(w.Project("ID_GOOD", true).Contains("Good"), "Previously working inaccessible definitions survive repeated refreshes and runtime reads.");
        }
        w.Import();
        Require(ContentDatabaseReader.ReadSkippedContent(w.Database).Count == 0, "Recovered access clears the diagnostics.");
    }

    internal static void LockedCorrectionAndRace()
    {
        using var w = new Workspace();
        string baseline = "<elements>" + Element("ID_FIX", "Original") + "</elements>";
        w.Write("core/source.xml", baseline, raw: true);
        w.Write("user/local/fix.xml", LocalCorrectionDocument.Create(baseline.Replace("Original", "Fixed"), baseline, "core/source.xml",
            [new LocalCorrection("fix", "replace", "ID_FIX", null, null)]), raw: true);
        using (var sourceLock = File.Open(w.PathFor("core/source.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Refuses(() => w.Import(), "Correction preparation failed");
            Require(!File.Exists(w.Database), "A readable correction must protect its inaccessible source even on the first import.");
        }
        w.Import(); string hash = w.Hash();
        foreach (string path in new[] { "core/source.xml", "user/local/fix.xml" })
        {
            using var locked = File.Open(w.PathFor(path), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Refuses(() => w.Import(), "protected correction input");
            Require(w.Hash() == hash, "Inaccessible known correction inputs preserve the existing database.");
        }
        w.Write("core/new.xml", Element("ID_NEW", "New"));
        using var newLock = File.Open(w.PathFor("core/new.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Refuses(() => w.Import(progress: new InlineProgress(p => { if (p.Phase == ContentImportPhase.Activating) newLock.Dispose(); })), "changed during sync");
        Require(w.Hash() == hash, "A previously uncaptured input becoming readable before activation requires a retry.");
    }
    private sealed class InlineProgress(Action<ContentImportProgress> report) : IProgress<ContentImportProgress>
    { public void Report(ContentImportProgress value) => report(value); }

    internal static void CliSkipOption()
    {
        using var w = new Workspace();
        w.Write("core/good.xml", Element("ID_GOOD", "Good")); w.Write("core/bad.xml", "<elements><broken", raw: true);
        foreach (bool skip in new[] { false, true })
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(AuroraTranslator.Program).Assembly.Location); start.ArgumentList.Add("sqlite-import");
            if (skip) start.ArgumentList.Add("--skip-unusable");
            start.ArgumentList.Add(w.Root); start.ArgumentList.Add(w.Database);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            Require(process.WaitForExit(60000), "CLI timed out");
            Require((process.ExitCode == 0) == skip, error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
            Require(File.Exists(w.Database) == skip, "The CLI must stay strict by default and support explicit best-effort import.");
        }
        Require(w.Project("ID_GOOD").Contains("Good"), "The CLI imports the usable content.");
    }
}
