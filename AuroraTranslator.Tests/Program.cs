using AuroraTranslator;
using AuroraTranslator.Models;
using Microsoft.Data.Sqlite;

var tests = new (string Name, Action Body)[]
{
    ("imports spellcasting profile entries", SpellcastingProfileEntryTests.ImportCreatesNormalizedEntries),
    ("migrates spellcasting profile entries", SpellcastingProfileEntryTests.MigrationRebuildsNormalizedEntries),
    ("repairs missing spellcasting profile entries", SpellcastingProfileEntryTests.CurrentVersionMaintenanceRebuildsMissingEntries),
    ("previews nested feat choices", NestedFeatPreviewTests.FeatPoolOptionsExposeOneLevelSelectPreviews),
    ("filters already-owned feat choices", NestedFeatPreviewTests.FeatPoolsExcludeOwnedFeatsOutsideTheirChosenSlot),
    ("filters already-owned dynamic choices", DynamicChoiceFilteringTests.DynamicPoolsExcludeOwnedOptionsOutsideTheirChosenSlot),
    ("filters requirement-gated dynamic choices", DynamicChoiceFilteringTests.DynamicPoolsMarkUnsatisfiedRequirementsUnavailable),
    ("filters already-owned fixed choices", FixedChoiceFilteringTests.FixedPoolsExcludeOwnedOptionsOutsideTheirChosenSlot)
};

if (args.Length > 0)
{
    tests = tests
        .Where(test => test.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (tests.Length == 0)
        throw new ArgumentException($"No tests matched filter '{args[0]}'.", nameof(args));
}

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

    public static void CurrentVersionMaintenanceRebuildsMissingEntries()
    {
        using var workspace = TestWorkspace.Create();

        AuroraSqliteImporter.Import(
            CreateCatalog(workspace),
            TestPaths.SchemaPath,
            workspace.DatabasePath);

        using (var connection = Open(workspace.DatabasePath))
        {
            TestAssert.Equal(10L, ExecuteLong(connection, "SELECT data_version FROM database_metadata WHERE singleton_id = 1;"));
            ExecuteNonQuery(connection, "DELETE FROM spellcasting_profile_entries;");
            TestAssert.Equal(0L, ExecuteLong(connection, "SELECT COUNT(*) FROM spellcasting_profile_entries;"));
        }

        AuroraSqliteImporter.ListContentPackages(workspace.DatabasePath, TestPaths.SchemaPath);

        using var repaired = Open(workspace.DatabasePath);
        TestAssert.Equal(10L, ExecuteLong(repaired, "SELECT data_version FROM database_metadata WHERE singleton_id = 1;"));
        TestAssert.Sequence(
            new[] { "wizard", "spell (fire, cold)", "spell [earth, air]", "spell {light, dark}" },
            QueryStrings(repaired, "SELECT entry_text FROM spellcasting_profile_entries WHERE entry_kind = 'list' ORDER BY ordinal;"));
        TestAssert.Sequence(
            new[] { "cleric", "druid" },
            QueryStrings(repaired, "SELECT entry_text FROM spellcasting_profile_entries WHERE entry_kind = 'extend' ORDER BY ordinal;"));
        TestAssert.Equal(
            6L,
            ExecuteLong(repaired, "SELECT COUNT(*) FROM v_spellcasting_profile_entries WHERE owner_aurora_id = 'ID_TEST_SPELLCASTING_CLASS';"));
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

    public static void FeatPoolsExcludeOwnedFeatsOutsideTheirChosenSlot()
    {
        using var workspace = TestWorkspace.Create();

        AuroraCharacterStateDocument document = AuroraCharacterStateDocument.Load(
            TestPaths.DataPath("character-state-early-fighter-example.json"));
        document.Feats.Add(new AuroraCharacterStateSelection
        {
            AuroraId = "ID_WOTC_PHB24_FEAT_ATHLETE",
            Name = "Athlete",
            PackageKey = "core-players-handbook-2024"
        });

        string statePath = Path.Combine(workspace.DirectoryPath, "fighter-with-athlete.json");
        File.WriteAllText(statePath, System.Text.Json.JsonSerializer.Serialize(document));

        CharacterEvaluationResult ownedResult = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            statePath);
        CharacterSelectResult asiSelect = ownedResult.AvailableSelects.Single(select =>
            string.Equals(select.ChoiceFamily, "asi-pick", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult featOption = asiSelect.Options.Single(option =>
            string.Equals(option.OptionAuroraId, "SEMANTIC_FEAT", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult ownedAthlete = featOption.FollowUpOptions?.Single(option =>
            string.Equals(option.OptionAuroraId, "ID_WOTC_PHB24_FEAT_ATHLETE", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Fighter ASI feat pool did not contain Athlete.");

        TestAssert.Equal(true, ownedAthlete.IsAlreadyOwned);
        TestAssert.Equal(false, ownedAthlete.IsAvailable);
        TestAssert.Equal("Already owned.", ownedAthlete.UnavailableReason);

        CharacterEvaluationResult chosenResult = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            TestPaths.DataPath("character-state-human-magic-initiate-example.json"));
        CharacterSelectResult versatileSelect = chosenResult.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Versatile", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.ChoiceFamily, "feat-pick", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult chosenMagicInitiate = versatileSelect.Options.Single(option =>
            string.Equals(option.OptionAuroraId, "ID_WOTC_PHB24_FEAT_MAGIC_INITIATE", StringComparison.OrdinalIgnoreCase));

        TestAssert.Equal(true, chosenMagicInitiate.IsAlreadyOwned);
        TestAssert.Equal(true, chosenMagicInitiate.IsChosenForSelect);
        TestAssert.Equal(true, chosenMagicInitiate.IsAvailable);
        TestAssert.Equal<string?>(null, chosenMagicInitiate.UnavailableReason);
    }
}

internal static class DynamicChoiceFilteringTests
{
    public static void DynamicPoolsExcludeOwnedOptionsOutsideTheirChosenSlot()
    {
        CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            TestPaths.DataPath("character-state-early-fighter-example.json"));

        CharacterSelectResult acolyteLanguageSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Acolyte", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Language (Acolyte)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult chosenDraconic = FindOption(acolyteLanguageSelect, "ID_LANGUAGE_DRACONIC");
        TestAssert.Equal(true, chosenDraconic.IsAlreadyOwned);
        TestAssert.Equal(true, chosenDraconic.IsChosenForSelect);
        TestAssert.Equal(true, chosenDraconic.IsAvailable);

        CharacterSelectResult humanLanguageSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Human", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Language (Human)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult crossSlotDraconic = FindOption(humanLanguageSelect, "ID_LANGUAGE_DRACONIC");
        TestAssert.Equal(true, crossSlotDraconic.IsAlreadyOwned);
        TestAssert.Equal(false, crossSlotDraconic.IsChosenForSelect);
        TestAssert.Equal(false, crossSlotDraconic.IsAvailable);
        TestAssert.Equal("Already owned.", crossSlotDraconic.UnavailableReason);

        CharacterSelectResult fighterSkillSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Fighter", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Skill Proficiency (Fighter)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult chosenAthletics = FindOption(fighterSkillSelect, "ID_PROFICIENCY_SKILL_ATHLETICS");
        TestAssert.Equal(true, chosenAthletics.IsAlreadyOwned);
        TestAssert.Equal(true, chosenAthletics.IsChosenForSelect);
        TestAssert.Equal(true, chosenAthletics.IsAvailable);

        CharacterSelectOptionResult grantedInsight = FindOption(fighterSkillSelect, "ID_PROFICIENCY_SKILL_INSIGHT");
        TestAssert.Equal(true, grantedInsight.IsAlreadyOwned);
        TestAssert.Equal(false, grantedInsight.IsChosenForSelect);
        TestAssert.Equal(false, grantedInsight.IsAvailable);
        TestAssert.Equal("Already owned.", grantedInsight.UnavailableReason);
    }

    public static void DynamicPoolsMarkUnsatisfiedRequirementsUnavailable()
    {
        using var workspace = TestWorkspace.Create();
        File.Copy(TestPaths.FirstPartyRegressionDatabasePath, workspace.DatabasePath);

        const string unsatisfiedRequirement = "ID_INTERNAL_TEST_REQUIREMENT_UNMET";
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = workspace.DatabasePath }.ToString()))
        {
            connection.Open();
            AddRequirement(connection, "ID_LANGUAGE_DWARVISH", unsatisfiedRequirement);
            AddRequirement(connection, "ID_PROFICIENCY_SKILL_ACROBATICS", unsatisfiedRequirement);
        }

        CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(
            workspace.DatabasePath,
            TestPaths.DataPath("character-state-early-fighter-example.json"));

        CharacterSelectResult humanLanguageSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Human", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Language (Human)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult blockedDwarvish = FindOption(humanLanguageSelect, "ID_LANGUAGE_DWARVISH");
        TestAssert.Equal(false, blockedDwarvish.IsAlreadyOwned);
        TestAssert.Equal(false, blockedDwarvish.IsChosenForSelect);
        TestAssert.Equal(false, blockedDwarvish.IsAvailable);
        TestAssert.Equal(unsatisfiedRequirement, blockedDwarvish.RequirementText);
        TestAssert.Equal("Requirements not satisfied.", blockedDwarvish.UnavailableReason);

        CharacterSelectResult fighterSkillSelect = result.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Fighter", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Skill Proficiency (Fighter)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult blockedAcrobatics = FindOption(fighterSkillSelect, "ID_PROFICIENCY_SKILL_ACROBATICS");
        TestAssert.Equal(false, blockedAcrobatics.IsAlreadyOwned);
        TestAssert.Equal(false, blockedAcrobatics.IsChosenForSelect);
        TestAssert.Equal(false, blockedAcrobatics.IsAvailable);
        TestAssert.Equal(unsatisfiedRequirement, blockedAcrobatics.RequirementText);
        TestAssert.Equal("Requirements not satisfied.", blockedAcrobatics.UnavailableReason);
    }

    private static CharacterSelectOptionResult FindOption(CharacterSelectResult select, string auroraId)
        => select.Options.Single(option =>
            string.Equals(option.OptionAuroraId, auroraId, StringComparison.OrdinalIgnoreCase));

    private static void AddRequirement(SqliteConnection connection, string auroraId, string requirementText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO element_requirements (element_id, ordinal, requirement_text)
SELECT
    e.element_id,
    COALESCE(
        (
            SELECT MAX(er.ordinal)
            FROM element_requirements AS er
            WHERE er.element_id = e.element_id
        ),
        -1
    ) + 1,
    $requirement_text
FROM elements AS e
WHERE e.aurora_id = $aurora_id;";
        command.Parameters.AddWithValue("$aurora_id", auroraId);
        command.Parameters.AddWithValue("$requirement_text", requirementText);

        int rowsInserted = command.ExecuteNonQuery();
        if (rowsInserted != 1)
            throw new InvalidOperationException($"Expected to add one requirement for {auroraId}, inserted {rowsInserted}.");
    }
}

internal static class FixedChoiceFilteringTests
{
    public static void FixedPoolsExcludeOwnedOptionsOutsideTheirChosenSlot()
    {
        CharacterEvaluationResult fighterResult = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            TestPaths.DataPath("character-state-early-fighter-example.json"));

        CharacterSelectResult archetypeSelect = fighterResult.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Martial Archetype", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Martial Archetype", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult ownedChampion = FindOption(archetypeSelect, "ID_WOTC_PHB_ARCHETYPE_CHAMPION");
        TestAssert.Equal(true, ownedChampion.IsAlreadyOwned);
        TestAssert.Equal(false, ownedChampion.IsChosenForSelect);
        TestAssert.Equal(false, ownedChampion.IsAvailable);
        TestAssert.Equal("Already owned.", ownedChampion.UnavailableReason);

        CharacterSelectResult fightingStyleSelect = fighterResult.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Fighting Style", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Fighting Style", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult chosenDefense = FindOption(
            fightingStyleSelect,
            "ID_WOTC_PHB_CLASS_FEATURE_FIGHTINGSTYLE_DEFENSE");
        TestAssert.Equal(true, chosenDefense.IsAlreadyOwned);
        TestAssert.Equal(true, chosenDefense.IsChosenForSelect);
        TestAssert.Equal(true, chosenDefense.IsAvailable);

        using var workspace = TestWorkspace.Create();
        AuroraCharacterStateDocument document = AuroraCharacterStateDocument.Load(
            TestPaths.DataPath("character-state-human-magic-initiate-example.json"));
        document.Elements.Add(new AuroraCharacterStateSelection
        {
            AuroraId = "ID_WOTC_PHB24_FEAT_MAGIC_INITIATE_CLERIC",
            Name = "Cleric",
            PackageKey = "core-players-handbook-2024"
        });

        string statePath = Path.Combine(workspace.DirectoryPath, "human-with-magic-initiate-cleric.json");
        File.WriteAllText(statePath, System.Text.Json.JsonSerializer.Serialize(document));

        CharacterEvaluationResult supportLinkedResult = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            statePath);
        CharacterSelectResult spellListSelect = supportLinkedResult.AvailableSelects.Single(select =>
            string.Equals(select.OwnerName, "Magic Initiate", StringComparison.OrdinalIgnoreCase)
            && string.Equals(select.SelectName, "Spell List (Magic Initiate)", StringComparison.OrdinalIgnoreCase));
        CharacterSelectOptionResult ownedCleric = FindOption(
            spellListSelect,
            "ID_WOTC_PHB24_FEAT_MAGIC_INITIATE_CLERIC");
        TestAssert.Equal(true, ownedCleric.IsAlreadyOwned);
        TestAssert.Equal(false, ownedCleric.IsChosenForSelect);
        TestAssert.Equal(false, ownedCleric.IsAvailable);
        TestAssert.Equal("Already owned.", ownedCleric.UnavailableReason);
    }

    private static CharacterSelectOptionResult FindOption(CharacterSelectResult select, string auroraId)
        => select.Options.Single(option =>
            string.Equals(option.OptionAuroraId, auroraId, StringComparison.OrdinalIgnoreCase));
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
