using AuroraTranslator;
using Microsoft.Data.Sqlite;

internal static class ParentAndSourceTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal void Write(string path, string xml) { var target = Path.Combine(Root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, xml); }
        internal void Import() => AuroraSqliteImporter.ImportFinalized(AuroraTranslator.Program.BuildAuroraImportCatalog(Root), TestPaths.SchemaPath, Database);
        internal string Query(string sql)
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()); c.Open();
            using var command = c.CreateCommand(); command.CommandText = sql; return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal string Parent(string id) => Query($"SELECT COALESCE(p.aurora_id,'') FROM features f JOIN elements e ON e.element_id=f.element_id LEFT JOIN elements p ON p.element_id=f.parent_element_id WHERE e.aurora_id='{id}'");
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }
    private static void Equal(string expected, string actual) { if (actual != expected) throw new Exception($"Expected '{expected}', got '{actual}'"); }

    private static string Infuse(string id, string family) => $"<elements><element id='{id}' name='Infuse Item' type='Class Feature' source='Fixture'><rules><select type='Class Feature' name='Infusion' supports='{family}' /></rules></element></elements>";

    internal static void FamilyParents()
    {
        using var f = new Fixture();
        f.Write("supplements/eberron/infuse.xml", Infuse("ID_ERLW_INFUSE", "Artificer Infusion, !TCOE Base"));
        f.Write("unearthed-arcana/infuse.xml", Infuse("ID_UA_INFUSE", "UA Artificer Infusion"));
        f.Write("reddit/options.xml", "<elements><element id='ID_ARCANE_CORD' name='Arcane Cord' type='Class Feature' source='Blazing Dawn Player’s Companion'><supports>Artificer Infusion</supports></element></elements>");
        f.Import(); Equal("ID_ERLW_INFUSE", f.Parent("ID_ARCANE_CORD"));
        string ua = f.Query("SELECT package_key FROM content_packages WHERE package_key LIKE 'unearthed%'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, ua, precedenceRank: 9999);
        Equal("ID_ERLW_INFUSE", f.Parent("ID_ARCANE_CORD"));
        f.Write("supplements/tasha/infuse.xml", Infuse("ID_TCOE_INFUSE", "Artificer Infusion"));
        f.Import(); Equal("", f.Parent("ID_ARCANE_CORD"));
        Equal("2", f.Query("SELECT COUNT(*) FROM parent_relationship_candidates c JOIN elements e ON e.element_id=c.owner_element_id WHERE e.aurora_id='ID_ARCANE_CORD'"));
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, ua, precedenceRank: 1);
        Equal("", f.Parent("ID_ARCANE_CORD"));
        AuroraSqliteImporter.RefreshPackageResolution(f.Database);
        Equal("", f.Parent("ID_ARCANE_CORD"));
        Equal("0", f.Query("SELECT COUNT(*) FROM element_support_links l JOIN elements e ON e.element_id=l.element_id WHERE e.aurora_id='ID_ARCANE_CORD' AND is_primary_parent=1"));
    }

    internal static void DirectParents()
    {
        using var f = new Fixture();
        f.Write("core/links.xml", """
            <elements>
              <element id="ID_WRONG" name="ID_PARENT" type="Class Feature" source="Fixture" />
              <element id="ID_PARENT" name="Correct parent" type="Class Feature" source="Fixture" />
              <element id="ID_CHILD" name="Child" type="Class Feature" source="Fixture"><supports>ID_PARENT</supports></element>
              <element id="ID_MISSING_NAME" name="ID_MISSING" type="Class Feature" source="Fixture" />
              <element id="ID_ORPHAN" name="Orphan" type="Class Feature" source="Fixture"><supports>ID_MISSING</supports></element>
              <element id="ID_WRONG_CLASS" name="Wrong class" type="Class" source="Fixture" />
              <element id="ID_CLASS" name="Correct class" type="Class" source="Fixture" />
              <element id="ID_ARCHETYPE" name="Archetype" type="Archetype" source="Fixture"><supports>ID_CLASS</supports></element>
              <element id="ID_BACKGROUND" name="Parent Background" type="Background" source="Fixture"><rules><grant type="Background Feature" id="ID_BACKGROUND_FEATURE" /></rules></element>
              <element id="ID_UNRELATED_BACKGROUND" name="Unrelated Background" type="Background" source="Fixture" />
              <element id="ID_BACKGROUND_FEATURE" name="Background Feature" type="Background Feature" source="Fixture"><supports>Background Feature</supports></element>
              <element id="ID_WRONG_RACE" name="ID_RACE" type="Race" source="Fixture" />
              <element id="ID_RACE" name="Correct race" type="Race" source="Fixture" />
              <element id="ID_SUBRACE" name="Subrace" type="Sub Race" source="Fixture"><supports>ID_RACE</supports></element>
              <element id="ID_VARIANT" name="Variant" type="Race Variant" source="Fixture"><supports>ID_RACE</supports></element>
              <element id="ID_BACKGROUND_VARIANT" name="Background Variant" type="Background Variant" source="Fixture"><supports>ID_BACKGROUND</supports></element>
            </elements>
            """);
        f.Import(); Equal("ID_PARENT", f.Parent("ID_CHILD")); Equal("", f.Parent("ID_ORPHAN"));
        Equal("ID_CLASS", f.Query("SELECT p.aurora_id FROM archetypes a JOIN elements p ON p.element_id=a.parent_class_element_id"));
        Equal("ID_BACKGROUND", f.Parent("ID_BACKGROUND_FEATURE"));
        Equal("ID_RACE", f.Query("SELECT p.aurora_id FROM subraces s JOIN elements p ON p.element_id=s.race_element_id"));
        Equal("ID_RACE", f.Query("SELECT p.aurora_id FROM race_variants v JOIN elements p ON p.element_id=v.race_element_id"));
        Equal("ID_BACKGROUND", f.Query("SELECT p.aurora_id FROM background_variants v JOIN elements p ON p.element_id=v.background_element_id"));
        AuroraSqliteImporter.RefreshPackageResolution(f.Database);
        Equal("ID_BACKGROUND", f.Parent("ID_BACKGROUND_FEATURE"));
    }

    internal static void RuleBasedSelectors()
    {
        using var f = new Fixture();
        f.Write("unearthed-arcana/selector.xml", """
            <elements>
              <element id="ID_UA_SELECTOR" name="Experimental Crafting" type="Class Feature" source="UA">
                <rules><select type="Class Feature" supports="Artificer Infusion" /></rules>
              </element>
              <element id="ID_UA_WRONG" name="Infuse Item" type="Class Feature" source="UA">
                <rules><select type="Class Feature" supports="UA Artificer Infusion" /></rules>
              </element>
              <element id="ID_NAME_ONLY" name="Infuse Item" type="Class Feature" source="UA" />
              <element id="ID_NO_SUPPORT" name="Infuse Item" type="Class Feature" source="UA"><rules><select type="Class Feature" /></rules></element>
              <element id="ID_DYNAMIC_SUPPORT" name="Infuse Item" type="Class Feature" source="UA"><rules><select type="Class Feature" supports="Artificer Infusion, !$(excluded)" /></rules></element>
              <element id="ID_INVALID_SUPPORT" name="Infuse Item" type="Class Feature" source="UA"><rules><select type="Class Feature" supports="(Artificer Infusion" /></rules></element>
            </elements>
            """);
        f.Write("user/options.xml", """
            <elements>
              <element id="ID_OPTION" name="Renamed infusion" type="Class Feature" source="Homebrew"><supports>Unrelated, Artificer Infusion</supports></element>
              <element id="ID_EXCLUDED" name="Excluded infusion" type="Class Feature" source="Homebrew"><supports>Artificer Infusion, Blocked</supports></element>
              <element id="ID_NAME_COLLISION" name="Artificer Infusion" type="Class Feature" source="Homebrew" />
              <element id="ID_WRONG_TYPE" name="Wrong type" type="Spell" source="Homebrew"><supports>Artificer Infusion</supports></element>
            </elements>
            """);
        f.Import(); Equal("ID_UA_SELECTOR", f.Parent("ID_OPTION"));
        Equal("", f.Parent("ID_NAME_COLLISION"));
        Equal("2", f.Query("SELECT COUNT(*) FROM parent_selector_diagnostics WHERE diagnostic_status='deferred'"));
        Equal("1", f.Query("SELECT COUNT(*) FROM parent_selector_diagnostics WHERE diagnostic_status='actionable'"));
        Equal("1", f.Query("SELECT COUNT(*) FROM v_selectable_options WHERE owner_aurora_id='ID_UA_SELECTOR' AND option_aurora_id='ID_OPTION'"));
        Equal("0", f.Query("SELECT COUNT(*) FROM v_selectable_options WHERE owner_aurora_id='ID_UA_SELECTOR' AND option_aurora_id='ID_NAME_COLLISION'"));
        string ua = f.Query("SELECT package_key FROM content_packages WHERE package_key LIKE 'unearthed%'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, ua, isEnabled: false);
        Equal("ID_UA_SELECTOR", f.Parent("ID_OPTION"));
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, ua, isEnabled: true, precedenceRank: 9999);
        Equal("ID_UA_SELECTOR", f.Parent("ID_OPTION"));
        Equal("1", f.Query("SELECT COUNT(*) FROM v_selectable_options WHERE owner_aurora_id='ID_UA_SELECTOR' AND option_aurora_id='ID_OPTION'"));
        Equal("0", f.Query("SELECT COUNT(*) FROM v_selectable_options WHERE owner_aurora_id='ID_UA_SELECTOR' AND option_aurora_id='ID_NAME_COLLISION'"));
        f.Write("supplements/publisher/selector.xml", """
            <elements>
              <element id="ID_PUBLISHED_SELECTOR" name="Third party crafting" type="Class Feature" source="Published">
                <rules><select type="Class Feature" supports="(Artificer Infusion || Other Family), !Blocked" /></rules>
              </element>
              <element id="ID_DIRECT_SELECTOR" name="Fixed choices" type="Class Feature" source="Published">
                <rules><select type="Class Feature"><item id="ID_OPTION" /></select></rules>
              </element>
              <element id="ID_GRANTING_PARENT" name="Granted option" type="Class Feature" source="Published">
                <rules><grant type="Class Feature" id="ID_OPTION" /></rules>
              </element>
              <element id="ID_ROOT_CLASS" name="Unrelated class name" type="Class" source="Published">
                <rules><grant type="Class Feature" id="ID_SUBCLASS_SELECTOR" /></rules>
              </element>
              <element id="ID_SUBCLASS_SELECTOR" name="Unrelated selector name" type="Class Feature" source="Published">
                <rules><select type="Archetype" supports="A subclass family" /></rules>
              </element>
              <element id="ID_RULE_ARCHETYPE" name="Unrelated subclass name" type="Archetype" source="Published"><supports>A subclass family</supports></element>
            </elements>
            """);
        f.Import(); Equal("", f.Parent("ID_OPTION"));
        Equal("4", f.Query("SELECT COUNT(*) FROM parent_relationship_candidates c JOIN elements e ON e.element_id=c.owner_element_id WHERE e.aurora_id='ID_OPTION'"));
        Equal("ID_UA_SELECTOR", f.Parent("ID_EXCLUDED"));
        Equal("ID_ROOT_CLASS", f.Query("SELECT p.aurora_id FROM archetypes a JOIN elements p ON p.element_id=a.parent_class_element_id"));
        Equal("0", f.Query("SELECT COUNT(*) FROM parent_relationship_candidates c JOIN elements p ON p.element_id=c.parent_element_id WHERE p.aurora_id IN ('ID_UA_WRONG','ID_NAME_ONLY','ID_NO_SUPPORT','ID_DYNAMIC_SUPPORT','ID_INVALID_SUPPORT')"));
        AuroraSqliteImporter.RefreshPackageResolution(f.Database);
        Equal("4", f.Query("SELECT COUNT(*) FROM parent_relationship_candidates c JOIN elements e ON e.element_id=c.owner_element_id WHERE e.aurora_id='ID_OPTION'"));
        Equal("ID_UA_SELECTOR", f.Parent("ID_EXCLUDED"));
    }

    internal static void GrantParentRefresh()
    {
        using var f = new Fixture();
        f.Write("core/parent.xml", "<elements><element id='ID_GRANT_PARENT' name='Parent' type='Class' source='Fixture'><rules><grant type='Class Feature' id='ID_GRANT_CHILD'/></rules></element></elements>");
        f.Write("user/child.xml", "<elements><element id='ID_GRANT_CHILD' name='Child' type='Class Feature' source='Fixture'/></elements>");
        f.Import(); Equal("ID_GRANT_PARENT", f.Parent("ID_GRANT_CHILD"));
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='core'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: false);
        Equal("ID_GRANT_PARENT", f.Parent("ID_GRANT_CHILD"));
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: true);
        Equal("ID_GRANT_PARENT", f.Parent("ID_GRANT_CHILD"));
        AuroraSqliteImporter.RefreshPackageResolution(f.Database);
        Equal("ID_GRANT_PARENT", f.Parent("ID_GRANT_CHILD"));
        f.Query("DROP VIEW v_ambiguous_parent_relationships; DROP TABLE parent_relationship_candidates; UPDATE features SET parent_element_id=NULL; SELECT 1");
        string childPackage = f.Query("SELECT package_key FROM content_packages WHERE package_kind='homebrew'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, childPackage, precedenceRank: 725);
        Equal("ID_GRANT_PARENT", f.Parent("ID_GRANT_CHILD"));
    }

    internal static void SourceClassification()
    {
        using var f = new Fixture();
        f.Write("unearthed-arcana/ua.xml", "<elements><element id='ID_UA' name='UA' type='Item' source='UA'/></elements>");
        f.Write("user/local/mine.xml", "<elements><element id='ID_LOCAL' name='Local' type='Item' source='Mine'/></elements>");
        f.Write("reddit/reddit-unearthed-arcana/test.xml", "<elements><element id='ID_REDDIT' name='Community UA' type='Item' source='Community'/></elements>");
        f.Write("supplements/publisher/source.xml", "<elements><element id='ID_THIRD' name='Published Supplement' type='Source' source='Core'><setters><set name='supplement'>true</set><set name='third-party'>true</set></setters></element></elements>");
        f.Write("third-party/dms-guild/source.xml", "<elements><element id='ID_PUBLISHED_HOMEBREW_FLAG' name='Published independent book' type='Source' source='Core'><setters><set name='homebrew'>true</set></setters></element></elements>");
        f.Write("supplements/wizards/source.xml", "<elements><element id='ID_WOTC' name='Wizards Supplement' type='Source' source='Core'><setters><set name='official'>true</set><set name='supplement'>true</set></setters></element></elements>");
        f.Write("supplements/wizards/items.xml", "<elements><element id='ID_WOTC_ITEM' name='Item' type='Item' source='Wizards Supplement'/></elements>");
        f.Write("ryoko/source.xml", "<elements><element id='ID_RYOKO' name='Ryoko' type='Source' source='Core'><setters><set name='third-party'>true</set><set name='supplement'>true</set></setters></element></elements>");
        f.Write("ryoko/helpers.xml", "<elements><element id='ID_REPRINT' name='Reprinted helper' type='Proficiency' source='Wizards Supplement'/></elements>");
        f.Write("collection/source.xml", "<elements><element id='ID_COLLECTION_SOURCE' name='Collection Homebrew' type='Source' source='Homebrew'><setters><set name='homebrew'>true</set></setters></element></elements>");
        f.Write("collection/home.xml", "<elements><element id='ID_COLLECTION_HOME' name='Homebrew' type='Item' source='Collection Homebrew'/><element id='ID_CITED_HELPER' name='Cited helper' type='Proficiency' source='Wizards Supplement'/></elements>");
        f.Import();
        string Kind(string id) => f.Query($"SELECT cp.package_kind FROM elements e JOIN source_files sf ON sf.source_file_id=e.source_file_id JOIN content_packages cp ON cp.content_package_id=sf.content_package_id WHERE e.aurora_id='{id}'");
        Equal("official", Kind("ID_UA")); Equal("homebrew", Kind("ID_LOCAL")); Equal("homebrew", Kind("ID_REDDIT"));
        Equal("third-party", Kind("ID_THIRD")); Equal("official", Kind("ID_WOTC_ITEM")); Equal("third-party", Kind("ID_RYOKO"));
        Equal("third-party", Kind("ID_REPRINT"));
        Equal("third-party", Kind("ID_PUBLISHED_HOMEBREW_FLAG"));
        f.Query("UPDATE content_packages SET precedence_rank=777,is_enabled=0,package_kind='local'; SELECT 1");
        f.Write("collection/published-source.xml", "<elements><element id='ID_COLLECTION_PUBLISHED_SOURCE' name='Collection Published Book' type='Source' source='Core'><setters><set name='official'>true</set></setters></element></elements>");
        f.Write("collection/published.xml", "<elements><element id='ID_COLLECTION_PUBLISHED' name='Published' type='Item' source='Collection Published Book'/></elements>");
        f.Import(); Equal("official", Kind("ID_UA")); Equal("homebrew", Kind("ID_LOCAL")); Equal("official", Kind("ID_WOTC_ITEM"));
        Equal("homebrew", Kind("ID_COLLECTION_HOME")); Equal("homebrew", Kind("ID_CITED_HELPER")); Equal("official", Kind("ID_COLLECTION_PUBLISHED"));
        Equal("2", f.Query("SELECT COUNT(*) FROM content_packages WHERE package_key LIKE 'collection-collection--%'"));
        Equal("0", f.Query("SELECT COUNT(*) FROM content_packages WHERE precedence_rank<>777 OR is_enabled<>0"));
    }
}
