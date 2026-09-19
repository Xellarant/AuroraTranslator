using AuroraTranslator;
using AuroraTranslator.Content;
using Builder.Data.Files;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class AppendPreparationTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal void Write(string path, string xml)
        { path = Path.Combine(Root, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, xml); }
        internal void Import(Action<string>? damage = null) => LocalCorrectionSync.ImportAsync([Root], Database, (roots, candidate, _) =>
        {
            AuroraSqliteImporter.ImportFinalized(AuroraTranslator.Program.BuildAuroraImportCatalog(roots[0]), TestPaths.SchemaPath, candidate);
            damage?.Invoke(candidate);
            return Task.FromResult(new CorrectionImportResult(true));
        }).GetAwaiter().GetResult();
        internal string Query(string sql) => Sql(Database, sql);
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }
    private static string Sql(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var q = c.CreateCommand(); q.CommandText = sql; return Convert.ToString(q.ExecuteScalar()) ?? ""; }
    private static void Equal(string expected, string actual) => TestAssert.Equal(expected, actual);

    internal static void EffectsAndPreferences()
    {
        using var f = new Fixture();
        f.Write("core/base.xml", """
          <elements>
            <element id="ID_PHB_SPELL_ACID_SPLASH" name="Acid Splash" type="Spell" source="PHB"><supports>Wizard</supports><setters><set name="level">0</set></setters></element>
            <element id="ID_PROFICIENCY_WEAPON_PROFICIENCY_MARTIAL_RANGED_WEAPONS" name="Martial ranged" type="Proficiency" source="PHB"/>
            <element id="ID_GUN" name="Firearm" type="Proficiency" source="DMG"/>
          </elements>
          """);
        f.Write("unearthed-arcana/extensions.xml", """
          <elements>
            <append id="ID_PHB_SPELL_ACID_SPLASH"><supports>UA Artificer, Wizard</supports></append>
            <append id="ID_PROFICIENCY_WEAPON_PROFICIENCY_MARTIAL_RANGED_WEAPONS"><rules><grant type="Proficiency" id="ID_GUN" level="3" requirements="[level:3]" /></rules></append>
          </elements>
          """);
        f.Import();
        void Check()
        {
            Equal("1", f.Query("SELECT COUNT(*) FROM spell_access WHERE access_text='UA Artificer'"));
            Equal("1", f.Query("SELECT COUNT(*) FROM grants g JOIN elements e ON e.element_id=g.target_element_id WHERE e.aurora_id='ID_GUN' AND g.grant_level=3 AND g.requirements_text='[level:3]'"));
            Equal("2", f.Query("SELECT COUNT(*) FROM content_append_operations WHERE status='applied'"));
            Equal("official", f.Query("SELECT DISTINCT package_kind FROM v_content_append_operations"));
            Equal("3", f.Query("SELECT COUNT(*) FROM resolved_elements_cache"));
        }
        Check();
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='core'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: false);
        Check(); f.Import(); Check();
        Equal("1", f.Query("SELECT COUNT(*) FROM content_packages WHERE is_enabled=0"));
        Equal("unrestricted", f.Query("SELECT catalog_policy FROM content_preparation_metadata"));
        using (var c = new SqliteConnection($"Data Source={f.Database};Mode=ReadOnly;Pooling=False"))
        {
            c.Open();
            var filtered = PreparedCatalogReader.Read(c, s => s.PackageKind == "core");
            TestAssert.Equal(false, filtered.Elements.Single(e => e.AuroraId == "ID_PHB_SPELL_ACID_SPLASH").Xml.Contains("UA Artificer"));
            TestAssert.Equal(false, filtered.Elements.Single(e => e.AuroraId == "ID_PROFICIENCY_WEAPON_PROFICIENCY_MARTIAL_RANGED_WEAPONS").Xml.Contains("<grant"));
            var all = PreparedCatalogReader.Read(c);
            TestAssert.Equal(true, all.Elements.Single(e => e.AuroraId == "ID_PHB_SPELL_ACID_SPLASH").Xml.Contains("UA Artificer"));
            TestAssert.Equal(true, PreparedCatalogReader.InputsMatch(c, [f.Root]));
        }
        Check(); // Filtering one request did not modify catalog rows or later requests.
        f.Write("unearthed-arcana/extensions.xml", "<elements/>"); f.Import();
        Equal("0", f.Query("SELECT COUNT(*) FROM spell_access WHERE access_text='UA Artificer'"));
        Equal("0", f.Query("SELECT COUNT(*) FROM grants"));
    }

    internal static void CorrectionsBeforeExtensions()
    {
        using var f = new Fixture();
        string baseline = "<elements><element id='ID_BASE' name='Base' type='Class Feature' source='Test'><description>old</description></element></elements>";
        f.Write("core/base.xml", baseline);
        f.Write("user/local/base.xml", LocalCorrectionDocument.Create(baseline.Replace(">old<", ">corrected<"), baseline, "core/base.xml", [new("fix", "replace", "ID_BASE", null, null)]));
        f.Write("user/append.xml", "<elements><append id='ID_BASE'><description><p>additional</p></description><supports>Local</supports></append></elements>");
        f.Import();
        string effective = f.Query("SELECT effective_xml FROM content_prepared_elements");
        TestAssert.Equal(true, effective.Contains("corrected") && effective.Contains("additional"));
        Equal("1", f.Query("SELECT COUNT(*) FROM element_supports WHERE support_text='Local'"));
        Equal("review-pending", f.Query("SELECT state FROM local_corrections"));
        Equal("homebrew", f.Query("SELECT package_kind FROM v_content_append_operations"));
        f.Import(); Equal(effective, f.Query("SELECT effective_xml FROM content_prepared_elements"));
    }

    internal static void UnresolvedAndFailedCandidate()
    {
        using var f = new Fixture();
        f.Write("core/base.xml", "<elements><element id='ID_REAL' name='Missing' type='Class Feature' source='Test'/></elements>");
        f.Write("user/append.xml", "<elements><append id='ID_MISSING'><supports>Must not guess</supports></append></elements>");
        f.Import(); Equal("unresolved-target", f.Query("SELECT status FROM content_append_operations"));
        Equal("0", f.Query("SELECT COUNT(*) FROM element_supports"));
        using (var c = new SqliteConnection($"Data Source={f.Database};Mode=ReadOnly;Pooling=False"))
        {
            c.Open();
            var hostSource = new PreparedCatalogSource("resource://builtins", "runtime/builtins.xml", "runtime", "core");
            var host = new PreparedCatalogElement("ID_MISSING", hostSource, "<element id='ID_MISSING' name='Host' type='Class Feature'/>");
            var projection = PreparedCatalogReader.Read(c, hostDefinitions: [host]);
            Equal("0", projection.UnresolvedAppends.Count.ToString());
            TestAssert.Equal(true, projection.Elements.Single(e => e.AuroraId == "ID_MISSING").Xml.Contains("Must not guess"));
            Equal("1", PreparedCatalogReader.Read(c).UnresolvedAppends.Count.ToString());
            var excluded = PreparedCatalogReader.Read(c, s => s.PackageKey == "runtime", hostDefinitions:
                [new("ID_REAL", hostSource, "<element id='ID_REAL' name='Host duplicate' type='Class Feature'/>")]);
            Equal("0", excluded.Elements.Count.ToString()); // Host must not reintroduce an excluded catalog ID.
        }
        f.Write("user/append.xml", "<elements><append id='ID_REAL'><rules><grant type='Class Feature' id='ID_REAL'/></rules></append></elements>");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Database)));
        try { f.Import(p => Sql(p, "DELETE FROM grants; SELECT 1")); throw new Exception("Expected validation failure"); }
        catch (InvalidDataException ex) { TestAssert.Equal(true, ex.Message.Contains("effects were lost")); }
        Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Database))));
        f.Write("user/append.xml", "<elements><append id='ID_REAL' type='Spell'><supports>Wrong type</supports></append></elements>");
        try { f.Import(); throw new Exception("Expected type conflict"); }
        catch (InvalidDataException ex) { TestAssert.Equal(true, ex.Message.Contains("append-conflict")); }
    }

    internal static void ProtectedAliases()
    {
        using var f = new Fixture();
        string baseline = "<elements><element id='ID_PARENT' name='Parent' type='Class' source='Book'><rules><grant type='Class Feature' id='ID_CHILD'/></rules></element><element id='ID_CHILD' name='Child' type='Class Feature' source='Book'><description>original</description></element></elements>";
        var plan = ProtectedLocalAliases.Create(baseline, baseline.Replace(">original<", ">local change<"), "core/book.xml", "BOOK");
        Equal("ID_PARENT_LOCAL_BOOK", plan.Aliases["ID_PARENT"]);
        f.Write("core/book.xml", baseline); f.Write("user/local/variants.xml", plan.ProtectedXml);
        f.Import();
        Equal("4", f.Query("SELECT COUNT(*) FROM elements"));
        Equal("2", f.Query("SELECT COUNT(*) FROM elements e JOIN source_files sf ON sf.source_file_id=e.source_file_id JOIN content_packages cp ON cp.content_package_id=sf.content_package_id WHERE cp.package_kind='homebrew'"));
        Equal("2", f.Query("SELECT COUNT(*) FROM local_corrections WHERE operation='add' AND state='review-pending'"));
        Equal("ID_CHILD_LOCAL_BOOK", f.Query("SELECT g.target_aurora_id FROM grants g JOIN rule_scopes rs ON rs.rule_scope_id=g.rule_scope_id JOIN elements e ON e.element_id=rs.owner_element_id WHERE e.aurora_id='ID_PARENT_LOCAL_BOOK'"));
        f.Write("core/book.xml", baseline.Replace(">original<", ">upstream revision<")); f.Import();
        TestAssert.Equal(true, f.Query("SELECT effective_xml FROM content_prepared_elements WHERE aurora_id='ID_CHILD'").Contains("upstream revision"));
        TestAssert.Equal(true, f.Query("SELECT effective_xml FROM content_prepared_elements WHERE aurora_id='ID_CHILD_LOCAL_BOOK'").Contains("local change"));
    }

    internal static void GenericTypes()
    {
        using var f = new Fixture();
        f.Write("user/types.xml", """
            <elements>
              <element id="ID_ACTION" type="Action" name="Brace" source="Test"><description>Action description</description><rules><stat name="fixture" value="1"/></rules></element>
              <element id="ID_CATEGORY" type="Weapon Category" name="Category" source="Test"><supports>Exact category</supports></element>
              <element id="ID_PLURAL" type="Feat Features" name="Plural" source="Test"><rules><grant type="Weapon Category" id="ID_CATEGORY"/></rules></element>
            </elements>
            """);
        f.Import();
        void Check()
        {
            Equal("3", f.Query("SELECT COUNT(*) FROM elements"));
            Equal("3", f.Query("SELECT COUNT(*) FROM v_unrecognized_element_type_diagnostics"));
            Equal("Feat Features", f.Query("SELECT et.type_name FROM elements e JOIN element_types et ON et.element_type_id=e.element_type_id WHERE e.aurora_id='ID_PLURAL'"));
            Equal("0", f.Query("SELECT COUNT(*) FROM features"));
            Equal("1", f.Query("SELECT COUNT(*) FROM stats WHERE stat_name='fixture'"));
            Equal("ID_CATEGORY", f.Query("SELECT e.aurora_id FROM grants g JOIN elements e ON e.element_id=g.target_element_id"));
        }
        Check(); f.Import(); Check();
        // Simulate the old writer's skipped-type rows and unchanged file hash.
        f.Query("PRAGMA foreign_keys=ON; DELETE FROM elements; DELETE FROM element_types WHERE loader_family='generic-unrecognized'; SELECT 1");
        f.Import(); Check();
    }

    internal static void SpellRules()
    {
        using var f = new Fixture();
        f.Write("core/spell.xml", """
            <elements>
              <element id="ID_PHB_SPELL_FIND_FAMILIAR" name="Find Familiar" type="Spell" source="PHB">
                <supports>Wizard</supports><description>Base description</description>
                <requirements>ID_KNOWN</requirements><sheet><description>Sheet text</description></sheet>
                <rules><select type="Companion" name="Familiar" supports="Familiar"/></rules>
              </element>
              <element id="ID_COMPANION" name="Owl" type="Companion" source="PHB"><supports>Familiar</supports></element>
            </elements>
            """);
        f.Write("user/extension.xml", """
            <elements><append id="ID_PHB_SPELL_FIND_FAMILIAR"><supports>Other List</supports>
              <rules><grant type="Companion" id="ID_COMPANION"/><stat name="spell-fixture" value="1"/></rules>
            </append></elements>
            """);
        f.Import();
        void Check()
        {
            Equal("1", f.Query("SELECT COUNT(*) FROM selects WHERE name_text='Familiar'"));
            Equal("1", f.Query("SELECT COUNT(*) FROM v_selectable_options WHERE owner_aurora_id='ID_PHB_SPELL_FIND_FAMILIAR' AND option_aurora_id='ID_COMPANION'"));
            Equal("1", f.Query("SELECT COUNT(*) FROM grants WHERE target_aurora_id='ID_COMPANION' AND target_element_id IS NOT NULL"));
            Equal("1", f.Query("SELECT COUNT(*) FROM stats WHERE stat_name='spell-fixture'"));
            Equal("1", f.Query("SELECT compendium_display FROM elements WHERE aurora_id='ID_PHB_SPELL_FIND_FAMILIAR'"));
            Equal("1", f.Query("SELECT COUNT(*) FROM element_texts WHERE text_kind='sheet' AND body='Sheet text'"));
            Equal("1", f.Query("SELECT COUNT(*) FROM element_texts WHERE text_kind='description' AND body='Base description'"));
        }
        Check(); f.Import(); Check();
    }
}
