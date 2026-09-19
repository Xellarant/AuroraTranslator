using AuroraTranslator;
using Microsoft.Data.Sqlite;
using System.Text.Json;

internal static class AuditRegressionTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal void Write(string path, string xml)
        {
            string target = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, xml);
        }
        internal void Import() => AuroraSqliteImporter.ImportFinalized(AuroraCatalogBuilder.BuildAuroraImportCatalog(Root), TestPaths.SchemaPath, Database);
        internal string Query(string sql)
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()); c.Open();
            using var command = c.CreateCommand(); command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal CharacterEvaluationResult Evaluate(AuroraCharacterStateChoice? choice = null, AuroraCharacterStateSelection? selection = null, AuroraCharacterStateSelection? extraSelection = null)
        {
            var state = new AuroraCharacterStateDocument { Elements = [selection ?? new() { AuroraId = "ID_OWNER" }] };
            if (extraSelection != null) state.Elements.Add(extraSelection);
            if (choice != null) state.SelectedChoices.Add(choice);
            string path = Path.Combine(workspace.DirectoryPath, "state.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            return AuroraCharacterStateEngine.Evaluate(Database, path);
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static Fixture SupportFixture()
    {
        var f = new Fixture();
        f.Write("unearthed-arcana/owner.xml", """
            <elements><element id="ID_OWNER" name="Experimental crafting" type="Class Feature" source="UA">
              <rules>
                <select name="Conjunction" type="Class Feature" supports="Family, !Blocked" />
                <select name="Alternative" type="Class Feature" supports="(Family || Alternate), !Blocked" />
              </rules>
            </element></elements>
            """);
        f.Write("user/options.xml", """
            <elements>
              <element id="ID_GOOD" name="Good" type="Class Feature" source="Homebrew"><supports>Family</supports></element>
              <element id="ID_GOOD_TWO" name="Also good" type="Class Feature" source="Homebrew"><supports>Family</supports></element>
              <element id="ID_ALTERNATE" name="Alternate option" type="Class Feature" source="Homebrew"><supports>Alternate</supports></element>
              <element id="ID_BLOCKED" name="Blocked" type="Class Feature" source="Homebrew"><supports>Family, Blocked</supports></element>
              <element id="ID_WRONG_TYPE" name="Wrong type" type="Magic Item" source="Homebrew"><supports>Family</supports></element>
            </elements>
            """);
        f.Import(); return f;
    }

    internal static void SupportIndex()
    {
        using var f = SupportFixture();
        void Check()
        {
            TestAssert.Equal("ID_GOOD,ID_GOOD_TWO", f.Query("SELECT group_concat(option_aurora_id) FROM (SELECT DISTINCT option_aurora_id FROM v_selectable_options WHERE select_name='Conjunction' ORDER BY option_aurora_id)"));
            TestAssert.Equal("ID_ALTERNATE,ID_GOOD,ID_GOOD_TWO", f.Query("SELECT group_concat(option_aurora_id) FROM (SELECT DISTINCT option_aurora_id FROM v_selectable_options WHERE select_name='Alternative' ORDER BY option_aurora_id)"));
        }
        Check();
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='homebrew'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: false);
        Check(); // Availability is a query-time app concern, not catalog membership.
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: true);
        Check();
        AuroraSqliteImporter.RefreshPackageResolution(f.Database); Check();
    }

    internal static void RuntimeSupports()
    {
        using var f = SupportFixture();
        // A legacy snapshot can have incomplete OR membership and overbroad
        // cached conjunctions. Runtime must still enforce the current contract.
        f.Query("DELETE FROM select_option_links; INSERT INTO select_option_links SELECT s.select_id,e.element_id,t.support_tag_id,'support-membership' FROM selects s CROSS JOIN elements e CROSS JOIN support_tags t WHERE e.aurora_id='ID_BLOCKED' AND t.support_text='Family'; SELECT 1");
        var result = f.Evaluate();
        TestAssert.Sequence(new[] { "ID_GOOD", "ID_GOOD_TWO" }, result.AvailableSelects.Single(s => s.SelectName == "Conjunction").Options.Select(o => o.OptionAuroraId!).Order().ToArray());
        TestAssert.Sequence(new[] { "ID_ALTERNATE", "ID_GOOD", "ID_GOOD_TWO" }, result.AvailableSelects.Single(s => s.SelectName == "Alternative").Options.Select(o => o.OptionAuroraId!).Order().ToArray());
    }

    internal static void ExplicitItemIdentity()
    {
        using var f = new Fixture();
        f.Write("core/owner.xml", """
            <elements><element id="ID_OWNER" name="Owner" type="Class Feature" source="Fixture">
              <rules><select type="Class Feature" name="Fixed"><item id="ID_MISSING">Collision</item><item id="ID_REAL">Collision</item><item>Legacy choice</item></select></rules>
              <extract><item id="ID_MISSING">Collision</item></extract>
            </element></elements>
            """);
        f.Write("user/options.xml", """
            <elements>
              <element id="ID_WRONG" name="Collision" type="Class Feature" source="Fixture" />
              <element id="ID_LEGACY" name="Legacy choice" type="Class Feature" source="Fixture" />
            </elements>
            """);
        f.Write("third-party/real/option.xml", "<elements><element id='ID_REAL' name='Real option' type='Class Feature' source='Fixture' /></elements>");
        f.Import();
        TestAssert.Equal("0", f.Query("SELECT COUNT(*) FROM select_items WHERE target_aurora_id='ID_MISSING' AND linked_element_id IS NOT NULL"));
        TestAssert.Equal("0", f.Query("SELECT COUNT(*) FROM element_extract_items WHERE target_aurora_id='ID_MISSING' AND linked_element_id IS NOT NULL"));
        TestAssert.Equal("1", f.Query("SELECT COUNT(*) FROM select_items WHERE target_aurora_id IS NULL AND linked_element_id IS NOT NULL"));
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='third-party'");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, isEnabled: false);
        TestAssert.Equal("1", f.Query("SELECT COUNT(*) FROM select_items WHERE target_aurora_id IS NOT NULL AND linked_element_id IS NOT NULL"));
        AuroraSqliteImporter.RefreshPackageResolution(f.Database);
        TestAssert.Equal("1", f.Query("SELECT COUNT(*) FROM select_items WHERE target_aurora_id IS NOT NULL AND linked_element_id IS NOT NULL"));
    }

    internal static void ChoiceIdentity()
    {
        using var f = new Fixture();
        f.Write("core/options.xml", """
            <elements>
              <element id="ID_OWNER" name="Owner" type="Class Feature" source="Fixture"><rules><select type="Class Feature" name="Fixed"><item id="ID_ONE"/><item id="ID_TWO"/></select></rules></element>
              <element id="ID_ONE" name="Shared name" type="Class Feature" source="Fixture" />
              <element id="ID_TWO" name="Shared name" type="Class Feature" source="Fixture" />
            </elements>
            """);
        f.Import();
        var select = f.Evaluate().AvailableSelects.Single();
        var missing = f.Evaluate(new() { ChoiceRowKey = select.ChoiceRowKey, OptionAuroraId = "ID_MISSING", OptionName = "Shared name" });
        TestAssert.Equal("option-not-found", missing.AppliedChoices.Single().Status);
        var ambiguous = f.Evaluate(new() { ChoiceRowKey = select.ChoiceRowKey, OptionName = "Shared name" });
        TestAssert.Equal("option-not-found", ambiguous.AppliedChoices.Single().Status);
        var staleRow = f.Evaluate(new() { ChoiceRowKey = select.ChoiceRowKey, OptionElementId = select.Options.Single(o => o.OptionAuroraId == "ID_ONE").OptionElementId, OptionAuroraId = "ID_TWO" });
        TestAssert.Equal("ID_TWO", staleRow.AppliedChoices.Single().OptionAuroraId);
    }

    internal static void ExplicitGrantIdentity()
    {
        using var f = new Fixture();
        f.Write("core/grants.xml", """
            <elements>
              <element id="ID_OWNER" name="Owner" type="Class" source="Fixture"><rules><grant type="Class Feature" id="ID_MISSING_CLASS_FEATURE_COLLISION"/></rules></element>
              <element id="ID_WRONG" name="Collision" type="Class Feature" source="Fixture"/>
            </elements>
            """);
        f.Import();
        TestAssert.Equal("0", f.Query("SELECT COUNT(*) FROM grants WHERE target_element_id IS NOT NULL"));
        TestAssert.Equal("1", f.Query("SELECT COUNT(*) FROM v_unresolved_loader_links WHERE link_kind='grant' AND unresolved_key='ID_MISSING_CLASS_FEATURE_COLLISION'"));
    }

    internal static void ScopedGrantIdentity()
    {
        using var f = new Fixture();
        f.Write("core/owner.xml", "<elements><element id='ID_OWNER' name='Owner' type='Class' source='Fixture'><rules><grant type='Class Feature' id='ID_CHILD'/></rules></element></elements>");
        f.Write("user/child.xml", "<elements><element id='ID_CHILD' name='Child' type='Class Feature' source='Fixture'/></elements>");
        f.Import();
        string key = f.Query("SELECT package_key FROM content_packages WHERE package_kind='core'");
        string childKey = f.Query("SELECT package_key FROM content_packages WHERE package_kind='homebrew'");
        void Check(string expected) => TestAssert.Equal(expected, f.Query("SELECT COALESCE(e.aurora_id,'') FROM grants g LEFT JOIN elements e ON e.element_id=g.target_element_id WHERE g.target_aurora_id='ID_CHILD'"));
        Check("ID_CHILD");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, key, precedenceRank: 321);
        Check("ID_CHILD");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, childKey, isEnabled: false);
        Check("ID_CHILD");
        AuroraSqliteImporter.UpdateContentPackageSettings(f.Database, childKey, isEnabled: true);
        Check("ID_CHILD");
        var parity = AuroraSqliteImporter.ValidatePackageRefreshParity(f.Database, key, precedenceRank: 456);
        TestAssert.Equal(true, parity.IsMatch);
        TestAssert.Equal(true, parity.TableResults.Any(t => t.TableName == "parent_relationship_candidates"));
        TestAssert.Equal(true, parity.TableResults.Any(t => t.TableName == "parent_selector_diagnostics"));
    }

    internal static void AmbiguousDirectSelection()
    {
        using var f = new Fixture();
        f.Write("core/options.xml", """
            <elements>
              <element id="ID_ONE" name="Shared name" type="Class Feature" source="Fixture"/>
              <element id="ID_TWO" name="Shared name" type="Class Feature" source="Fixture"/>
            </elements>
            """);
        f.Import();
        try { f.Evaluate(selection: new() { Name = "Shared name" }); }
        catch (InvalidDataException ex) when (ex.Message.Contains("ID_ONE") && ex.Message.Contains("ID_TWO")) { return; }
        throw new Exception("An ambiguous named selection must request a specific Aurora ID instead of selecting both definitions.");
    }

    internal static void DirectSelectionEligibility()
    {
        using var f = new Fixture();
        f.Write("core/subraces.xml", """
            <elements>
              <element id="ID_OWNER" name="Owner" type="Race" source="Fixture"><rules><select name="Subrace" type="Sub Race" supports="Family, !Blocked"/></rules></element>
              <element id="ID_GOOD" name="Good subrace" type="Sub Race" source="Fixture"><supports>Family</supports></element>
              <element id="ID_BLOCKED" name="Blocked subrace" type="Sub Race" source="Fixture"><supports>Family, Blocked</supports></element>
            </elements>
            """);
        f.Import();
        var result = f.Evaluate(extraSelection: new() { AuroraId = "ID_BLOCKED" });
        TestAssert.Equal(1, result.ComputedCharacter.PendingChoices.Single(c => c.SelectName == "Subrace").RemainingCount);
    }

    internal static void PrimarySupportIdentity()
    {
        using var f = new Fixture();
        f.Write("core/links.xml", """
            <elements>
              <element id="ID_PARENT" name="Parent" type="Class Feature" source="Fixture" />
              <element id="ID_CHILD" name="Child" type="Class Feature" source="Fixture"><supports>Unrelated tag, ID_PARENT</supports></element>
            </elements>
            """);
        f.Import();
        TestAssert.Equal("0", f.Query("SELECT COUNT(*) FROM element_support_links l JOIN support_tags t ON t.support_tag_id=l.support_tag_id WHERE t.support_text='Unrelated tag' AND (l.linked_element_id IS NOT NULL OR l.is_primary_parent=1)"));
        TestAssert.Equal("1", f.Query("SELECT COUNT(*) FROM element_support_links l JOIN support_tags t ON t.support_tag_id=l.support_tag_id WHERE t.support_text='ID_PARENT' AND l.is_primary_parent=1"));
    }

    internal static void ConflictingPublisherFlags()
    {
        using var f = new Fixture();
        f.Write("supplements/book/source.xml", """
            <elements><element id="ID_SOURCE" name="Conflicting publisher" type="Source" source="Fixture"><setters><set name="official">true</set><set name="third-party">true</set></setters></element></elements>
            """);
        try { f.Import(); }
        catch (InvalidDataException ex) when (ex.Message.Contains("Conflicting", StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Contradictory publisher flags must produce an actionable diagnostic.");
    }
}
