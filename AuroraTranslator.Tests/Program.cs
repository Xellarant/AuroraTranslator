using AuroraTranslator;
using AuroraTranslator.Models;
using Microsoft.Data.Sqlite;

var tests = new (string Name, Action Body)[]
{
    ("imports spellcasting profile entries", SpellcastingProfileEntryTests.ImportCreatesNormalizedEntries),
    ("migrates spellcasting profile entries", SpellcastingProfileEntryTests.MigrationRebuildsNormalizedEntries),
    ("previews nested feat choices", NestedFeatPreviewTests.FeatPoolOptionsExposeOneLevelSelectPreviews)
};

var failures = new List<string>();
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Failed tests:");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");

    Environment.Exit(1);
}

internal static class SpellcastingProfileEntryTests
{
    public static void ImportCreatesNormalizedEntries()
    {
        using var workspace = TestWorkspace.Create();

        AuroraSqliteImporter.Import(
            CreateCatalog(workspace),
            TestPaths.SchemaPath,
            workspace.DatabasePath);

        using var connection = Open(workspace.DatabasePath);

        TestAssert.Equal(10L, ExecuteLong(connection, "SELECT data_version FROM database_metadata WHERE singleton_id = 1;"));
        TestAssert.Sequence(
            new[] { "wizard", "spell (fire, cold)", "spell [earth, air]", "spell {light, dark}" },
            QueryStrings(connection, "SELECT entry_text FROM spellcasting_profile_entries WHERE entry_kind = 'list' ORDER BY ordinal;"));
        TestAssert.Sequence(
            new[] { "cleric", "druid" },
            QueryStrings(connection, "SELECT entry_text FROM spellcasting_profile_entries WHERE entry_kind = 'extend' ORDER BY ordinal;"));
        TestAssert.Equal(
            6L,
            ExecuteLong(connection, "SELECT COUNT(*) FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_SPELLCASTING_CLASS';"));
    }

    public static void MigrationRebuildsNormalizedEntries()
    {
        using var workspace = TestWorkspace.Create();

        AuroraSqliteImporter.Import(
            CreateCatalog(workspace),
            TestPaths.SchemaPath,
            workspace.DatabasePath);

        using (var connection = Open(workspace.DatabasePath))
        {
            ExecuteNonQuery(connection, "DROP VIEW IF EXISTS v_spellcasting_profile_entries;");
            ExecuteNonQuery(connection, "DROP TABLE spellcasting_profile_entries;");
            ExecuteNonQuery(connection, "UPDATE database_metadata SET data_version = 9 WHERE singleton_id = 1;");
        }

        AuroraSqliteImporter.ListContentPackages(workspace.DatabasePath, TestPaths.SchemaPath);

        using var migrated = Open(workspace.DatabasePath);
        TestAssert.Equal(10L, ExecuteLong(migrated, "SELECT data_version FROM database_metadata WHERE singleton_id = 1;"));
        TestAssert.Equal(1L, ExecuteLong(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'spellcasting_profile_entries';"));
        TestAssert.Sequence(
            new[] { "wizard", "spell (fire, cold)", "spell [earth, air]", "spell {light, dark}" },
            QueryStrings(migrated, "SELECT entry_text FROM spellcasting_profile_entries WHERE entry_kind = 'list' ORDER BY ordinal;"));
        TestAssert.Equal(
            6L,
            ExecuteLong(migrated, "SELECT COUNT(*) FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_SPELLCASTING_CLASS';"));
    }

    private static AuroraImportCatalog CreateCatalog(TestWorkspace workspace)
    {
        const string relativePath = "UnitTests/spellcasting.xml";
        string fullPath = Path.Combine(workspace.DirectoryPath, "spellcasting.xml");
        File.WriteAllText(fullPath, "<elements />");

        return new AuroraImportCatalog
        {
            Files =
            {
                new AuroraFileInfo
                {
                    RelativePath = relativePath,
                    FullPath = fullPath,
                    Name = "Unit Test Content",
                    Description = "Minimal spellcasting fixture"
                }
            },
            Elements =
            {
                new AuroraElement
                {
                    id = "ID_TEST_SPELLCASTING_CLASS",
                    name = "Test Spellcaster",
                    type = "Class",
                    source = "Unit Test Source",
                    index = "test-spellcaster",
                    source_file_path = relativePath,
                    spellcasting = new Spellcasting
                    {
                        name = "Test Spellcasting",
                        ability = "Intelligence",
                        extend = true,
                        list = Text("wizard, spell (fire, cold), spell [earth, air], spell {light, dark}"),
                        extendList = Text("cleric, druid"),
                        prepare = true,
                        allowReplace = false
                    }
                }
            }
        };
    }

    private static AuroraTextCollection Text(string raw)
        => new() { raw = raw };

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        return connection;
    }

    private static long ExecuteLong(SqliteConnection connection, string sql)
        => Convert.ToInt64(ExecuteScalar(connection, sql));

    private static object ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() ?? throw new InvalidOperationException($"No value returned for SQL: {sql}");
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> QueryStrings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));

        return values;
    }
}

internal static class NestedFeatPreviewTests
{
    public static void FeatPoolOptionsExposeOneLevelSelectPreviews()
    {
        CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            TestPaths.DataPath("character-state-human-magic-initiate-example.json"));

        CharacterSelectResult featSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.ChoiceFamily, "feat-pick", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.OwnerName, "Versatile", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Feat (Versatile)", StringComparison.OrdinalIgnoreCase));

        var expectedMagicInitiateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ID_WOTC_PHB24_FEAT_MAGIC_INITIATE",
            "ID_WOTC_PHB24_FEAT_MAGIC_INITIATE_2"
        };
        List<CharacterSelectOptionResult> magicInitiateOptions = featSelect.Options
            .Where(option => expectedMagicInitiateIds.Contains(option.OptionAuroraId ?? string.Empty))
            .OrderBy(option => option.OptionAuroraId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TestAssert.Equal(2, magicInitiateOptions.Count);
        foreach (CharacterSelectOptionResult option in magicInitiateOptions)
        {
            TestAssert.Equal("unlocked-selects", option.FollowUpKind);

            CharacterSelectOptionResult preview = option.FollowUpOptions?.SingleOrDefault(followUp =>
                string.Equals(followUp.OptionKind, "select-preview", StringComparison.OrdinalIgnoreCase)
                && string.Equals(followUp.OptionName, "Spell List (Magic Initiate) (Feat Feature)", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Magic Initiate option {option.OptionAuroraId} did not expose its spell-list preview.");

            TestAssert.Equal("select-preview:fixed-element-pool", preview.FollowUpKind);
            TestAssert.Equal(3, preview.FollowUpOptions?.Count ?? 0);

            if ((preview.FollowUpOptions ?? Array.Empty<CharacterSelectOptionResult>())
                .Any(nestedOption => nestedOption.FollowUpOptions?.Count > 0))
                throw new InvalidOperationException("Nested select preview options should not recursively expose deeper follow-up previews.");
        }
    }
}

internal static class TestAssert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void Sequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }
}

internal sealed class TestWorkspace : IDisposable
{
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "test.sqlite");

    private TestWorkspace(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    public static TestWorkspace Create()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "AuroraTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch
        {
        }
    }
}

internal static class TestPaths
{
    public static string SchemaPath { get; } = FindSchemaPath();
    public static string FirstPartyRegressionDatabasePath => DataPath("aurora-first-party-regression.sqlite");

    public static string DataPath(string fileName)
    {
        string dataPath = Path.Combine(FindRepositoryRoot(), "5eApiTranslator", "Data", fileName);
        if (!File.Exists(dataPath))
            throw new FileNotFoundException($"Could not find test data file: {dataPath}");

        return dataPath;
    }

    private static string FindSchemaPath()
        => DataPath("sqlite-character-loading.sql");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "5eApiTranslator", "Data", "sqlite-character-loading.sql");
            if (File.Exists(candidate))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repository root from the test output directory.");
    }
}
