using System.Security.Cryptography;
using System.Xml.Linq;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

internal static class AppendPolicyMigrationTests
{
    private const string BaseXml = """
        <elements>
          <element id="ID_BASE" name="Base" type="Class Feature" source="Test">
            <description>Base</description><rules><grant type="Class Feature" id="ID_FIRST"/></rules>
          </element>
          <element id="ID_FIRST" name="First" type="Class Feature" source="Test"/>
          <element id="ID_SUPPLEMENT" name="Supplement" type="Class Feature" source="Test"/>
          <element id="ID_HOMEBREW" name="Homebrew" type="Class Feature" source="Test"/>
        </elements>
        """;
    private const string SupplementXml = """
        <elements><append id="ID_BASE"><description><p>Supplement description</p></description>
          <rules><grant type="Class Feature" id="ID_SUPPLEMENT"/></rules></append></elements>
        """;
    private const string HomebrewXml = """
        <elements><append id="ID_BASE"><description><p>Homebrew description</p></description>
          <rules><grant type="Class Feature" id="ID_HOMEBREW"/></rules></append></elements>
        """;

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal string FreshDatabase => Path.Combine(workspace.DirectoryPath, "fresh.sqlite");
        internal string PathFor(string relative) => Path.GetFullPath(Path.Combine(Root, relative));
        internal void Write(string relative, string xml)
        {
            string path = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        internal ContentImportResult Import(string? database = null) => ContentImport.ImportAsync(
            Root, database ?? Database, skipUnusableContent: true).GetAwaiter().GetResult();
        internal SqliteConnection Open(string? database = null)
        {
            var connection = new SqliteConnection($"Data Source={database ?? Database};Pooling=False");
            connection.Open();
            return connection;
        }
        internal string Query(string sql, string? database = null)
        {
            using var connection = Open(database);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            command.ExecuteNonQuery();
        }
        internal string Hash()
        {
            // The old-policy seed deliberately calls the low-level writer, whose disposed
            // connections can remain pooled. Release those handles before comparing bytes.
            SqliteConnection.ClearAllPools();
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Database)));
        }

        internal void SeedOldMaterialization()
        {
            Write("core/base.xml", BaseXml);
            Write("supplements/b.xml", SupplementXml);
            Write("homebrew/a.xml", HomebrewXml);
            Import();

            // Materialize the actual old results through the finalized writer, so typed rows and
            // staged source hashes describe homebrew-before-supplements and combined descriptions.
            // This is deliberately more than downgrading version metadata on a corrected database.
            var oldDocument = XDocument.Parse(BaseXml);
            var target = oldDocument.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == "ID_BASE");
            foreach (string extension in new[] { HomebrewXml, SupplementXml })
            {
                var append = XDocument.Parse(extension).Root!.Element("append")!;
                foreach (string container in new[] { "description", "rules" })
                    target.Element(container)!.Add(append.Element(container)!.Elements().Select(e => new XElement(e)));
            }
            string oldStage = Path.Combine(workspace.DirectoryPath, "old-materialized");
            foreach (var (relative, xml) in new[]
            {
                ("core/base.xml", oldDocument.ToString(SaveOptions.DisableFormatting)),
                ("supplements/b.xml", "<elements />"), ("homebrew/a.xml", "<elements />")
            })
            {
                string path = Path.Combine(oldStage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, xml);
            }
            AuroraSqliteImporter.ImportFinalized(AuroraCatalogBuilder.BuildAuroraImportCatalog(oldStage), null, Database);
            Execute("""
                UPDATE content_prepared_elements SET effective_xml=$xml WHERE aurora_id='ID_BASE';
                UPDATE database_metadata SET data_version=17;
                UPDATE content_preparation_metadata SET contract_version=1;
                """, ("$xml", target.ToString(SaveOptions.DisableFormatting)));
            Require(GrantIds() == "ID_FIRST|ID_HOMEBREW|ID_SUPPLEMENT", "The fixture has old materialized grant order.");
            Require(Description().Contains("Homebrew description") && Description().Contains("Supplement description"),
                "The fixture has combined descriptions in normalized rows, not just old metadata.");
        }

        internal string GrantIds(string? database = null) => Query("""
            SELECT GROUP_CONCAT(target_aurora_id,'|') FROM (
              SELECT g.target_aurora_id FROM grants g
              JOIN rule_scopes s ON s.rule_scope_id=g.rule_scope_id
              JOIN elements e ON e.element_id=s.owner_element_id
              WHERE e.aurora_id='ID_BASE' AND e.declaration_status='effective' ORDER BY g.ordinal)
            """, database);
        internal string Description(string? database = null) => Query("""
            SELECT t.body FROM element_texts t JOIN resolved_elements_cache r ON r.winning_element_id=t.element_id
            WHERE r.aurora_id='ID_BASE' AND t.text_kind='description' AND t.ordinal=1
            """, database);
        internal string EffectiveXml(string? database = null) => Query(
            "SELECT effective_xml FROM content_prepared_elements WHERE aurora_id='ID_BASE'", database);
        internal string Projection(string? database = null)
        {
            using var connection = Open(database);
            return PreparedCatalogReader.Read(connection).Elements.Single(e => e.AuroraId == "ID_BASE").Xml;
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static void RefreshOldMaterialization()
    {
        using var f = new Fixture();
        f.SeedOldMaterialization();
        string original = File.ReadAllText(f.PathFor("core/base.xml"));
        Require(ContentDatabaseReader.IsStale([f.Root], f.Database), "An old policy is stale even with unchanged source XML.");
        using (var connection = f.Open())
        {
            Require(!PreparedCatalogReader.IsPrepared(connection), "The old prepared contract cannot pass current projection validation.");
            try { PreparedCatalogReader.Read(connection); throw new Exception("An old prepared snapshot was accepted."); }
            catch (InvalidDataException) { }
        }

        AuroraSqliteImporter.ListContentPackages(f.Database);
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='core' LIMIT 1");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: false);
        Require(f.Query("SELECT data_version FROM database_metadata") == "17", "Package maintenance cannot certify a new content projection.");
        Require(f.Query("SELECT contract_version FROM content_preparation_metadata") == "1", "Package maintenance cannot certify a new preparation policy.");
        Require(f.GrantIds() == "ID_FIRST|ID_HOMEBREW|ID_SUPPLEMENT", "Administration does not secretly recompose old content.");

        var refreshed = f.Import();
        Require(refreshed.FilesChanged > 0, "Changed prepared output must force the target file through the incremental writer.");
        Require(f.Query("SELECT data_version FROM database_metadata") == ContentDatabaseReader.CurrentDataVersion.ToString(), "Refresh advances the data version.");
        Require(!ContentDatabaseReader.IsStale([f.Root], f.Database), "Successful recomposition clears staleness.");
        Require(f.GrantIds() == "ID_FIRST|ID_SUPPLEMENT|ID_HOMEBREW" && f.Description() == "Base", "Refresh corrects order and description rows.");
        Require(File.ReadAllText(f.PathFor("core/base.xml")) == original, "Migration must not edit authoritative XML.");
        f.Import(f.FreshDatabase);
        Require(f.EffectiveXml() == f.EffectiveXml(f.FreshDatabase) && f.Projection() == f.Projection(f.FreshDatabase)
            && f.GrantIds() == f.GrantIds(f.FreshDatabase) && f.Description() == f.Description(f.FreshDatabase),
            "Migrated normalized and prepared results must equal a fresh import.");
        var detail = ContentCatalogReader.ReadDetail(f.Database, "ID_BASE")!;
        Require(detail.AppliedAppends.Select(a => a.Source.RelativePath.Replace('\\', '/'))
            .SequenceEqual(new[] { "supplements/b.xml", "homebrew/a.xml" }), "Provenance follows actual append application order.");
        string effective = f.EffectiveXml();
        Require(f.Import().FilesChanged == 0 && f.EffectiveXml() == effective, "A repeated import neither rewrites nor duplicates append effects.");
    }

    internal static void OldAppendRetentionRequiresRepair()
    {
        foreach (string history in new[] { "applied", "remembered", "retained diagnostic", "untyped" })
        {
            using var f = new Fixture();
            f.SeedOldMaterialization();
            string broken = history == "retained diagnostic" ? "core/base.xml" : "homebrew/a.xml";
            if (history != "applied")
            {
                // Old best-effort retention stored a flattened base, so a later refresh cannot
                // safely reapply current operations to it. The dependency may survive only in
                // supplier history or a retained-operation diagnostic.
                f.Execute("""
                    UPDATE content_prepared_elements SET base_xml=effective_xml WHERE aurora_id='ID_BASE';
                    UPDATE content_append_operations SET status='skipped',diagnostic=NULL;
                    """);
                if (history == "retained diagnostic")
                    f.Execute("UPDATE content_append_operations SET diagnostic='append-target-retained: retained old effective target';");
                else
                    f.Execute("INSERT INTO content_definition_suppliers VALUES ('ID_BASE',$path,'append');",
                        ("$path", f.PathFor("homebrew/a.xml")));
                if (history == "untyped")
                    f.Execute("""
                        ALTER TABLE content_definition_suppliers RENAME TO typed_suppliers;
                        CREATE TABLE content_definition_suppliers (aurora_id TEXT NOT NULL,file_path TEXT NOT NULL,PRIMARY KEY(aurora_id,file_path));
                        INSERT INTO content_definition_suppliers SELECT aurora_id,file_path FROM typed_suppliers;
                        DROP TABLE typed_suppliers;
                        """);
            }
            f.Write(broken, "<elements><broken");
            string previousHash = f.Hash();
            try { f.Import(); throw new Exception("Unsafe old append retention was accepted: " + history); }
            catch (InvalidDataException ex)
            {
                Require(ex.Message.Contains("ID_BASE") && ex.Message.Contains("older append policy")
                    && ex.Message.Contains("Repair") && (ex.Message.Contains("a.xml") || ex.Message.Contains("b.xml")),
                    "The diagnostic must identify the affected ID, supplier and repair action.");
            }
            Require(f.Hash() == previousHash, "An unsafe retained upgrade must preserve the working database byte-for-byte.");

            f.Write(broken, broken == "core/base.xml" ? BaseXml : HomebrewXml);
            f.Import();
            Require(f.Description() == "Base" && f.GrantIds() == "ID_FIRST|ID_SUPPLEMENT|ID_HOMEBREW",
                "Readable suppliers allow full recomposition and migration.");
            string current = f.EffectiveXml();
            f.Write("homebrew/a.xml", "<elements><broken");
            f.Import(); f.Import();
            Require(f.EffectiveXml() == current && XNode.DeepEquals(XElement.Parse(f.Projection()), XElement.Parse(current)),
                "The current append policy still permits repeated last-known-good retention.");
        }
        using (var f = new Fixture())
        {
            f.SeedOldMaterialization();
            File.Delete(f.PathFor("core/base.xml"));
            f.Write("homebrew/a.xml", "<elements><broken");
            f.Import();
            Require(f.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_BASE'") == "0"
                && f.Query("SELECT COUNT(*) FROM content_prepared_elements WHERE aurora_id='ID_BASE'") == "0",
                "A deliberately removed base must stay absent; an unreadable former append supplier cannot require retaining it.");
            Require(!ContentDatabaseReader.IsStale([f.Root], f.Database),
                "Removing an old appended base must permit migration because no old effective definition is retained.");
        }
    }

    internal static void OldUnextendedDefinitionCanBeRetained()
    {
        using var f = new Fixture();
        f.Write("core/base.xml", BaseXml);
        f.Import();
        string previous = f.EffectiveXml();
        f.Execute("UPDATE database_metadata SET data_version=17; UPDATE content_preparation_metadata SET contract_version=1;");
        f.Write("core/base.xml", "<elements><broken");
        f.Import();
        Require(f.EffectiveXml() == previous && XNode.DeepEquals(XElement.Parse(f.Projection()), XElement.Parse(previous)),
            "A previous ordinary definition without append dependencies remains safe to retain across this policy change.");
    }
}
