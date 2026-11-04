using System.Security.Cryptography;
using System.Xml.Linq;
using Aurora.Content;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;

internal static class PreparedSourceIntegrityTests
{
    private const string Casting = "<spellcasting name='Wizard' ability='Intelligence' prepare='false'><list known='true'>Wizard</list></spellcasting>";
    private static string Definition(string id, string name, string body = "", string type = "Class Feature") =>
        $"<element id='{id}' name='{name}' type='{type}' source='Fixture'>{body}</element>";
    private static string Document(params string[] elements) => "<elements>" + string.Concat(elements) + "</elements>";

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal string Write(string relative, string xml)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
            return path;
        }
        internal ContentImportResult Import(bool bestEffort = false) =>
            ContentImport.ImportAsync(Root, Database, skipUnusableContent: bestEffort).GetAwaiter().GetResult();
        internal string Sql(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal List<string> CheckReadOnly()
        {
            SqliteConnection.ClearAllPools();
            string[] paths = Directory.GetFiles(Root, "*.xml", SearchOption.AllDirectories).Append(Database).ToArray();
            var hashes = paths.ToDictionary(path => path, Hash);
            var failures = AuroraDataIntegrity.Check(Database, Root);
            SqliteConnection.ClearAllPools();
            Require(hashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value),
                "Integrity inspection must not modify source XML or the database.");
            return failures;
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Healthy(Fixture fixture) => TestAssert.Sequence(Array.Empty<string>(), fixture.CheckReadOnly());
    private static void HasFailure(Fixture fixture, string message) => Require(
        fixture.CheckReadOnly().Any(failure => failure.Contains(message, StringComparison.OrdinalIgnoreCase)),
        "Integrity inspection must diagnose " + message + ".");

    internal static void CorrectionsComparePreparedDefinitions()
    {
        using var fixture = new Fixture();
        string caster = Definition("ID_PREP_CASTER", "Original caster", Casting, "Class");
        string renamed = Definition("ID_PREP_OLD", "Original identity");
        string removed = Definition("ID_PREP_REMOVED", "Removed definition");
        string companion = Definition("ID_PREP_COMPANION", "Original companion");
        string baseline = Document(caster, renamed, removed, companion);
        string replacement = caster.Replace("Original caster", "Corrected caster").Replace("Intelligence", "Wisdom");
        string newIdentity = renamed.Replace("ID_PREP_OLD", "ID_PREP_NEW");
        string sourcePath = fixture.Write("core/base.xml", baseline.Replace("Original companion", "Current companion"));
        string correctionPath = fixture.Write("user/local/fix.xml", LocalCorrectionDocument.Create(
            Document(replacement, newIdentity, companion), baseline, "core/base.xml",
            [
                new("caster", "replace", "ID_PREP_CASTER", null, LocalCorrectionDocument.Fingerprint(XElement.Parse(caster))),
                new("rename", "rename", "ID_PREP_OLD", "ID_PREP_NEW", LocalCorrectionDocument.Fingerprint(XElement.Parse(renamed))),
                new("remove", "remove", "ID_PREP_REMOVED", null, LocalCorrectionDocument.Fingerprint(XElement.Parse(removed)))
            ]));
        fixture.Import();

        TestAssert.Equal("Corrected caster", fixture.Sql("SELECT name FROM elements WHERE aurora_id='ID_PREP_CASTER'"));
        TestAssert.Equal("Wisdom", fixture.Sql("SELECT ability_name FROM spellcasting_profiles"));
        TestAssert.Equal("Current companion", fixture.Sql("SELECT name FROM elements WHERE aurora_id='ID_PREP_COMPANION'"));
        TestAssert.Equal("1", fixture.Sql("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_PREP_NEW'"));
        TestAssert.Equal("0", fixture.Sql("SELECT COUNT(*) FROM elements WHERE aurora_id IN ('ID_PREP_OLD','ID_PREP_REMOVED')"));
        Healthy(fixture);

        var evaluation = LocalCorrectionDocument.Evaluate(File.ReadAllText(correctionPath), File.ReadAllText(sourcePath));
        File.WriteAllText(sourcePath, evaluation.EffectiveXml);
        LocalCorrectionDocument.AcceptUpstream(correctionPath,
            LocalCorrectionDocument.FileFingerprint(correctionPath), LocalCorrectionDocument.FileFingerprint(sourcePath),
            ["caster", "rename", "remove"]);
        string acceptedXml = File.ReadAllText(correctionPath);
        fixture.Import();
        Require(!File.Exists(correctionPath) && fixture.Sql("SELECT status FROM local_override_files") == "retired",
            "Successful acceptance and import retire redundant local XML while retaining its provenance.");
        Healthy(fixture);
        File.WriteAllText(correctionPath, acceptedXml);
        HasFailure(fixture, "retired");
    }

    internal static void AppendsCompareComposedSpellcasting()
    {
        using var fixture = new Fixture();
        fixture.Write("core/base.xml", Document(Definition("ID_PREP_APPEND", "Appended caster", type: "Class")));
        fixture.Write("supplements/extension.xml", Document("<append id='ID_PREP_APPEND'>" + Casting + "</append>"));
        fixture.Import();
        TestAssert.Equal("1", fixture.Sql("SELECT COUNT(*) FROM content_append_operations WHERE status='applied'"));
        TestAssert.Equal("Wizard", fixture.Sql("SELECT profile_name FROM spellcasting_profiles"));
        Healthy(fixture);
    }

    internal static void PreparedDecisionsRemainInspectable()
    {
        using var fixture = new Fixture();
        fixture.Write("core/base.xml", Document(
            Definition("ID_PREP_DUPLICATE", "Earlier caster", Casting, "Class"),
            Definition("ID_PREP_RETAINED", "Last usable caster", Casting, "Class")));
        fixture.Write("user/winner.xml", Document(Definition("ID_PREP_DUPLICATE", "Winning caster",
            Casting.Replace("Intelligence", "Wisdom"), "Class")));
        fixture.Write("core/identical.xml", Document(Definition("ID_PREP_DUPLICATE", "Winning caster",
            Casting.Replace("Intelligence", "Wisdom"), "Class")));
        fixture.Import(bestEffort: true);
        TestAssert.Equal("1", fixture.Sql("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_PREP_DUPLICATE' AND declaration_status='superseded'"));
        TestAssert.Equal("2", fixture.Sql("SELECT COUNT(*) FROM content_prepared_sources WHERE aurora_id='ID_PREP_DUPLICATE'"));
        Require(ContentDatabaseReader.ReadSkippedContent(fixture.Database).Any(issue => issue.Kind == "definition-collision"),
            "A structurally valid provisional choice must retain its conflict-review diagnostic.");
        string rowCount = fixture.Sql("SELECT COUNT(*) FROM elements");
        TestAssert.Equal(rowCount, fixture.Sql("SELECT element_count FROM database_metadata WHERE singleton_id=1"));
        Healthy(fixture);
        fixture.Import(bestEffort: true);
        TestAssert.Equal(rowCount, fixture.Sql("SELECT COUNT(*) FROM elements"));
        TestAssert.Equal(rowCount, fixture.Sql("SELECT element_count FROM database_metadata WHERE singleton_id=1"));
        Healthy(fixture);

        // Best-effort import deliberately keeps the verified effective declarations while
        // remembering the captured malformed supplier. Source diagnostics remain inspectable.
        fixture.Write("core/base.xml", "<elements><broken");
        fixture.Write("homebrew/unusable.xml", "<elements><broken");
        var imported = fixture.Import(bestEffort: true);
        Require(imported.Skipped.Count(issue => issue.Kind == "unreadable") >= 2,
            "Both retained and newly skipped suppliers must still be reported.");
        TestAssert.Equal("Last usable caster", fixture.Sql("SELECT name FROM elements WHERE aurora_id='ID_PREP_RETAINED'"));
        Healthy(fixture);
        Require(ContentDatabaseReader.ReadSkippedContent(fixture.Database).Any(issue => issue.Kind == "unreadable"),
            "An integrity check must not clear the import's retained/skipped-source warnings.");
    }

    internal static void PreparedProjectionCorruptionStillFails()
    {
        using var fixture = new Fixture();
        string original = Document(Definition("ID_PREP_CORRUPTION", "Original caster", Casting, "Class"));
        string corrected = original.Replace("Original caster", "Corrected caster").Replace("Intelligence", "Wisdom");
        fixture.Write("core/base.xml", original);
        string correctionPath = fixture.Write("user/local/fix.xml", LocalCorrectionDocument.Create(corrected, original,
            "core/base.xml", [new("caster", "replace", "ID_PREP_CORRUPTION", null, null)]));
        fixture.Import();
        Healthy(fixture);

        fixture.Sql("UPDATE database_metadata SET element_count=element_count+1 WHERE singleton_id=1");
        HasFailure(fixture, "metadata element count mismatch");
        fixture.Sql("UPDATE database_metadata SET element_count=element_count-1 WHERE singleton_id=1");
        Healthy(fixture);

        string contractVersion = fixture.Sql("SELECT contract_version FROM content_preparation_metadata WHERE singleton_id=1");
        fixture.Sql("UPDATE content_preparation_metadata SET contract_version=-1 WHERE singleton_id=1");
        HasFailure(fixture, "preparation");
        fixture.Sql($"UPDATE content_preparation_metadata SET contract_version={contractVersion} WHERE singleton_id=1");
        Healthy(fixture);
        fixture.Sql("DELETE FROM content_preparation_metadata WHERE singleton_id=1");
        HasFailure(fixture, "preparation");
        fixture.Sql($"INSERT INTO content_preparation_metadata VALUES(1,{contractVersion},'unrestricted','materialized')");
        Healthy(fixture);
        fixture.Sql("ALTER TABLE content_preparation_metadata RENAME TO removed_preparation_metadata");
        HasFailure(fixture, "preparation");
        fixture.Sql("ALTER TABLE removed_preparation_metadata RENAME TO content_preparation_metadata");
        Healthy(fixture);

        fixture.Sql("UPDATE elements SET name='Corrupted name' WHERE aurora_id='ID_PREP_CORRUPTION'");
        HasFailure(fixture, "identity mismatch");
        fixture.Sql("UPDATE elements SET name='Corrected caster' WHERE aurora_id='ID_PREP_CORRUPTION'");
        Healthy(fixture);

        string originalStatus = fixture.Sql("SELECT declaration_status FROM elements WHERE aurora_id='ID_PREP_CORRUPTION'");
        Require(originalStatus != "superseded", "The corrected definition starts as the active declaration.");
        fixture.Sql("UPDATE elements SET declaration_status='superseded' WHERE aurora_id='ID_PREP_CORRUPTION'");
        HasFailure(fixture, "status");
        fixture.Sql($"UPDATE elements SET declaration_status='{originalStatus}' WHERE aurora_id='ID_PREP_CORRUPTION'");
        Healthy(fixture);

        // Keep normalized fields consistent with the altered raw profile. This must still
        // disagree with the prepared declaration, rather than pass a self-consistency check.
        fixture.Sql("UPDATE spellcasting_profiles SET ability_name='Charisma',raw_xml=replace(raw_xml,'Wisdom','Charisma')");
        HasFailure(fixture, "spellcasting XML mismatch");
        fixture.Sql("UPDATE spellcasting_profiles SET ability_name='Wisdom',raw_xml=replace(raw_xml,'Charisma','Wisdom')");
        Healthy(fixture);

        byte[] before = File.ReadAllBytes(correctionPath);
        File.AppendAllText(correctionPath, "\n<!-- local edit after import -->\n");
        HasFailure(fixture, "hash mismatch");
        File.WriteAllBytes(correctionPath, before);
        Healthy(fixture);
    }
}
