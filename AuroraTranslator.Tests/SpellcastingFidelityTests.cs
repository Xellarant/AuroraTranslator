using AuroraTranslator;
using Microsoft.Data.Sqlite;

internal static class SpellcastingFidelityTests
{
    public static void XmlOwnershipAndContributions()
    {
        using var workspace = TestWorkspace.Create();
        string source = CreateSource(workspace);
        Import(source, workspace.DatabasePath);
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, source));
        using var connection = Open(workspace.DatabasePath);
        TestAssert.Equal(4L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_HEXBLADE';"));
        TestAssert.Sequence(new[] { "Superseded List", "Wizard,Evocation" }, Rows(connection, "SELECT entry_text FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_WIZARD' ORDER BY entry_ordinal;"));
        TestAssert.Sequence(new[] { "Wizard,Evocation" }, Rows(connection, "SELECT entry_text FROM v_spellcasting_list_contributions WHERE contribution_owner_aurora_id = 'ID_TEST_WIZARD' AND entry_kind = 'list';"));
        TestAssert.Equal(1L, Scalar(connection, "SELECT prepare_from_spell_list FROM v_spellcasting_definitions WHERE owner_aurora_id = 'ID_TEST_CLERIC';"));
        TestAssert.Equal(0L, Scalar(connection, "SELECT prepare_from_spell_list FROM v_spellcasting_definitions WHERE owner_aurora_id = 'ID_TEST_WIZARD';"));
        TestAssert.Sequence(new[] { "ID_TEST_WARLOCK_2014", "ID_TEST_WARLOCK_2024" }, Rows(connection,
            "SELECT recipient_owner_aurora_id FROM v_spellcasting_extension_targets WHERE extension_owner_aurora_id = 'ID_TEST_HEXBLADE' ORDER BY 1;"));
        TestAssert.Equal(4L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_extension_targets WHERE extension_owner_aurora_id = 'ID_TEST_STUDENT' AND binding_kind = 'all-profiles';"));
        TestAssert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_extension_targets WHERE extension_owner_aurora_id = 'ID_TEST_ORPHAN' AND binding_kind = 'unresolved' AND recipient_profile_id IS NULL;"));
        TestAssert.Equal(8L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_list_contributions WHERE contribution_owner_aurora_id = 'ID_TEST_HEXBLADE' AND requires_character_context = 1;"));
        TestAssert.Equal(2L, Scalar(connection, "SELECT COUNT(DISTINCT spell_element_id) FROM v_spellcasting_entry_spell_references WHERE resolution_status = 'resolved';"));
        TestAssert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_entry_spell_references WHERE resolution_status = 'unresolved';"));
        TestAssert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_HEXBLADE' AND is_known = 1 AND entry_text = 'Wizard,Evocation';"));
    }

    public static void PartialDamageAndSourceMismatch()
    {
        using var workspace = TestWorkspace.Create();
        string source = CreateSource(workspace);
        Import(source, workspace.DatabasePath);
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "DELETE FROM spellcasting_profile_entries WHERE entry_text = 'ID_TEST_SHIELD_2014';");
            Execute(connection, "UPDATE spellcasting_profile_entries SET is_known = 0 WHERE is_known = 1;");
        }
        TestAssert.Equal(true, AuroraDataIntegrity.Check(workspace.DatabasePath).Any(message => message.Contains("entry mismatch")));
        AuroraSqliteImporter.ListContentPackages(workspace.DatabasePath, TestPaths.SchemaPath);
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, source));
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "UPDATE spellcasting_profiles SET assign_to_all = 0 WHERE assign_to_all = 1;");
        }
        TestAssert.Equal(true, AuroraDataIntegrity.Check(workspace.DatabasePath).Any(message => message.Contains("profile mismatch")));
        using (var connection = Open(workspace.DatabasePath))
            Execute(connection, "UPDATE spellcasting_profiles SET assign_to_all = 1 WHERE owner_element_id IN (SELECT element_id FROM elements WHERE aurora_id = 'ID_TEST_STUDENT');");
        using (var connection = Open(workspace.DatabasePath))
            Execute(connection, "UPDATE elements SET name = 'Corrupted name' WHERE aurora_id = 'ID_TEST_CLERIC';");
        TestAssert.Equal(true, AuroraDataIntegrity.Check(workspace.DatabasePath, source).Any(message => message.Contains("Source element identity mismatch")));
        using (var connection = Open(workspace.DatabasePath))
            Execute(connection, "UPDATE elements SET name = 'Cleric' WHERE aurora_id = 'ID_TEST_CLERIC';");

        string path = Path.Combine(source, "core", "fixture.xml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("ability=\"Wisdom\"", "ability=\"Intelligence\""));
        TestAssert.Equal(true, AuroraDataIntegrity.Check(workspace.DatabasePath, source).Any(message => message.Contains("Source spellcasting XML mismatch")));
        Import(source, workspace.DatabasePath);
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, source));
    }

    public static void LegacyRefreshMatchesFreshImport()
    {
        using var workspace = TestWorkspace.Create();
        string source = CreateSource(workspace);
        Import(source, workspace.DatabasePath);
        using (var connection = Open(workspace.DatabasePath))
        {
            Execute(connection, "UPDATE database_metadata SET data_version = 10;");
            Execute(connection, "UPDATE spellcasting_profiles SET raw_xml = NULL, assign_to_all = NULL;");
            Execute(connection, "DELETE FROM spellcasting_profile_entries;");
        }
        AuroraSqliteImporter.ListContentPackages(workspace.DatabasePath, TestPaths.SchemaPath);
        using (var connection = Open(workspace.DatabasePath))
        {
            TestAssert.Equal(7L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_definitions WHERE requires_xml_reimport = 1;"));
            TestAssert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM v_spellcasting_extension_targets WHERE recipient_profile_id IS NOT NULL;"));
        }
        TestAssert.Equal(true, AuroraDataIntegrity.Check(workspace.DatabasePath).Any(message => message.Contains("requires XML reimport")));
        Import(source, workspace.DatabasePath);
        string fresh = Path.Combine(workspace.DirectoryPath, "fresh.sqlite");
        Import(source, fresh);
        using var migrated = Open(workspace.DatabasePath);
        using var original = Open(fresh);
        TestAssert.Sequence(Snapshot(original), Snapshot(migrated));
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, source));
        Import(source, workspace.DatabasePath);
        TestAssert.Sequence(Snapshot(original), Snapshot(migrated));
    }

    public static void PackageResolution()
    {
        using var workspace = TestWorkspace.Create();
        string source = CreateSource(workspace);
        string overlay = Path.Combine(source, "supplements", "override");
        Directory.CreateDirectory(overlay);
        File.WriteAllText(Path.Combine(overlay, "override.xml"), """
<elements><element id="ID_TEST_HEXBLADE" name="Hexblade Override" type="Archetype Feature" source="Fixture Overlay">
<spellcasting name="Wizard" extend="true"><extend>ID_TEST_SHIELD_2024</extend></spellcasting>
</element></elements>
""");
        Import(source, workspace.DatabasePath);
        string package;
        using (var connection = Open(workspace.DatabasePath))
            package = Rows(connection, "SELECT cp.package_key FROM content_packages cp JOIN source_files sf ON sf.content_package_id = cp.content_package_id WHERE sf.relative_path LIKE '%override.xml';").Single();
        AuroraSqliteImporter.UpdateContentPackageSettings(workspace.DatabasePath, package, precedenceRank: 9999, schemaPath: TestPaths.SchemaPath);
        using (var connection = Open(workspace.DatabasePath))
            TestAssert.Sequence(new[] { "ID_TEST_WIZARD" }, Rows(connection, "SELECT recipient_owner_aurora_id FROM v_spellcasting_extension_targets WHERE extension_owner_aurora_id = 'ID_TEST_HEXBLADE';"));
        AuroraSqliteImporter.UpdateContentPackageSettings(workspace.DatabasePath, package, isEnabled: false, schemaPath: TestPaths.SchemaPath);
        using (var connection = Open(workspace.DatabasePath))
            TestAssert.Sequence(new[] { "ID_TEST_WARLOCK_2014", "ID_TEST_WARLOCK_2024" }, Rows(connection, "SELECT recipient_owner_aurora_id FROM v_spellcasting_extension_targets WHERE extension_owner_aurora_id = 'ID_TEST_HEXBLADE' ORDER BY 1;"));
    }

    private static string CreateSource(TestWorkspace workspace)
    {
        string source = Path.Combine(workspace.DirectoryPath, "source");
        Directory.CreateDirectory(Path.Combine(source, "core"));
        File.Copy(TestPaths.DataPath("spellcasting-fidelity-example.xml"), Path.Combine(source, "core", "fixture.xml"));
        return source;
    }

    private static void Import(string source, string database)
        => AuroraSqliteImporter.Import(AuroraTranslator.Program.BuildAuroraImportCatalog(source), TestPaths.SchemaPath, database);
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }
    private static string[] Snapshot(SqliteConnection connection) => Rows(connection, """
SELECT owner_aurora_id || '|' || entry_kind || '|' || entry_ordinal || '|' || entry_text || '|' || is_known
FROM v_spellcasting_profile_entries
UNION ALL
SELECT extension_owner_aurora_id || '|' || COALESCE(recipient_owner_aurora_id, '') || '|' || binding_kind
FROM v_spellcasting_extension_targets ORDER BY 1;
""");
    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static string[] Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }
}
