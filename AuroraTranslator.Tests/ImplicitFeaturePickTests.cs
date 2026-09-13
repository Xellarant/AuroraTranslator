using AuroraTranslator;
using System.Text.Json;

internal static class ImplicitFeaturePickTests
{
    private const string RolePrefix = "ID_WOTC_PHB24_CLASS_FEATURE_DRUID_PRIMAL_ORDER_";

    public static void DirectPrimalOrderDoesNotAddCompetingRole()
    {
        foreach (string role in new[] { "MAGICIAN", "WARDEN" })
        {
            AuroraCharacterStateDocument document = Druid();
            document.Elements[0].AuroraId = RolePrefix + role;
            document.Elements[0].Name = null;
            CharacterEvaluationResult result = Evaluate(document);
            AssertRole(result, role);
            TestAssert.Equal(false, result.ComputedCharacter.PendingChoices.Any(choice => choice.SelectName == "Sacred Role (Primal Order)"));
            TestAssert.Equal(false, result.ComputedCharacter.Warnings.Any(warning => warning.WarningKind == "over-selected-choice"));
            TestAssert.Equal(role == "WARDEN", result.ActiveGrants.Any(grant => grant.TargetAuroraId == "ID_PROFICIENCY_ARMOR_PROFICIENCY_MEDIUM_ARMOR"));
        }
    }

    public static void ExplicitAndUnselectedPrimalOrder()
    {
        AuroraCharacterStateDocument document = Druid();
        document.Elements.Clear();
        CharacterEvaluationResult empty = Evaluate(document);
        TestAssert.Equal(0, empty.DirectSelections.Count(selection => selection.AuroraId.StartsWith(RolePrefix)));
        TestAssert.Equal(1, empty.ComputedCharacter.PendingChoices.Single(choice => choice.SelectName == "Sacred Role (Primal Order)").RemainingCount);
        CharacterSelectResult select = empty.AvailableSelects.Single(choice => choice.SelectName == "Sacred Role (Primal Order)");
        document.SelectedChoices.Add(new AuroraCharacterStateChoice
        {
            ChoiceRowKey = select.ChoiceRowKey,
            OptionAuroraId = RolePrefix + "MAGICIAN"
        });
        CharacterEvaluationResult chosen = Evaluate(document);
        AssertRole(chosen, "MAGICIAN");
        TestAssert.Equal("applied", chosen.AppliedChoices.Single().Status);
        TestAssert.Equal(false, chosen.ComputedCharacter.PendingChoices.Any(choice => choice.SelectName == "Sacred Role (Primal Order)"));
    }

    public static void DeterministicDefaultsRemainActive()
    {
        CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(
            TestPaths.FirstPartyRegressionDatabasePath,
            TestPaths.DataPath("character-state-monk-focus-example.json"));
        foreach (string name in new[] { "Flurry of Blows", "Patient Defense", "Step of the Wind" })
            TestAssert.Equal(true, result.ComputedCharacter.Features.Any(feature => feature.Name == name));
        TestAssert.Equal(false, result.ComputedCharacter.PendingChoices.Any(choice => choice.ChoiceFamily == "feature-pick" && choice.IsBlocking));
    }

    private static void AssertRole(CharacterEvaluationResult result, string role)
        => TestAssert.Sequence(new[] { RolePrefix + role }, result.DirectSelections
            .Where(selection => selection.AuroraId.StartsWith(RolePrefix))
            .Select(selection => selection.AuroraId).ToArray());

    public static void DefaultsRetainChoiceProvenance()
    {
        foreach (var scenario in new[] { (Name: "monk-focus", Count: 3), (Name: "life-domain", Count: 2) })
        {
            CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(
                TestPaths.FirstPartyRegressionDatabasePath,
                TestPaths.DataPath($"character-state-{scenario.Name}-example.json"));
            TestAssert.Equal(scenario.Count, result.ComputedCharacter.ChoiceSelections.Count);
            foreach (ComputedCharacterItemResult selection in result.ComputedCharacter.ChoiceSelections)
            {
                TestAssert.Equal(true, result.ComputedCharacter.Features.Any(feature => feature.Key == selection.Name));
                TestAssert.Equal(true, selection.Provenance.All(entry => !string.IsNullOrWhiteSpace(entry.OwnerName) && !string.IsNullOrWhiteSpace(entry.Detail)));
            }
        }
    }

    private static AuroraCharacterStateDocument Druid()
        => AuroraCharacterStateDocument.Load(TestPaths.DataPath("character-state-druid-primal-order-direct-example.json"));

    private static CharacterEvaluationResult Evaluate(AuroraCharacterStateDocument document)
    {
        using var workspace = TestWorkspace.Create();
        string path = Path.Combine(workspace.DirectoryPath, "state.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return AuroraCharacterStateEngine.Evaluate(TestPaths.FirstPartyRegressionDatabasePath, path);
    }
}
