using AuroraTranslator;

internal static class EvaluatorFixtureTests
{
    public static void OwnershipFixtures()
    {
        CharacterEvaluationResult cleric = Evaluate("life-domain-complete");
        AssertApplied(cleric);
        TestAssert.Equal(0, cleric.ComputedCharacter.PendingChoices.Count);
        TestAssert.Equal(true, cleric.ComputedCharacter.GrantedSpells.Any(spell =>
            spell.SpellAuroraId == "ID_WOTC_PHB24_SPELL_BLESS" && spell.IsPrepared == true));
        TestAssert.Equal(true, cleric.AppliedChoices.Any(choice =>
            choice.SelectName == "Level 1 Spell (Magic Initiate)" && choice.OptionAuroraId == "ID_WOTC_PHB24_SPELL_GUIDING_BOLT"));

        CharacterEvaluationResult monk = Evaluate("monk-complete");
        AssertApplied(monk);
        TestAssert.Equal(true, monk.AppliedChoices.Any(choice =>
            choice.SelectName == "Tool Proficiency (Monk)" && choice.OptionAuroraId == "ID_PROFICIENCY_TOOL_PROFICIENCY_FLUTE"));
        TestAssert.Equal(true, monk.ActiveGrants.Any(grant =>
            grant.TargetAuroraId == "ID_PROFICIENCY_TOOL_PROFICIENCY_CALLIGRAPHERS_SUPPLIES"));
        TestAssert.Equal(false, monk.ComputedCharacter.PendingChoices.Any(choice =>
            choice.SelectName is "Tool Proficiency (Monk)" or "Skill Proficiency (Monk)" or "Language (Human)"));

        foreach (string fixture in new[] { "rogue-classic-expertise", "rogue-classic-expertise-complete" })
        {
            CharacterEvaluationResult rogue = Evaluate(fixture);
            AssertApplied(rogue);
            TestAssert.Equal(4, rogue.AppliedChoices.Count(choice => choice.SelectName == "Skill Proficiency (Rogue)"));
            TestAssert.Equal(true, rogue.ActiveGrants.Any(grant => grant.TargetAuroraId == "ID_PROFICIENCY_SKILL_PERCEPTION"));
            TestAssert.Equal(false, rogue.ComputedCharacter.PendingChoices.Any(choice => choice.SelectName == "Skill Proficiency (Rogue)"));
            if (fixture.EndsWith("-complete"))
            {
                TestAssert.Equal(2, rogue.AppliedChoices.Count(choice => choice.SelectName == "Expertise (Rogue)"));
                TestAssert.Equal(false, rogue.ComputedCharacter.PendingChoices.Any(choice => choice.SelectName == "Expertise (Rogue)"));
            }
            else
                TestAssert.Equal(2, rogue.ComputedCharacter.PendingChoices.Single(choice => choice.SelectName == "Expertise (Rogue)").RemainingCount);
        }
    }

    public static void SpellChoiceIdentities()
    {
        foreach (var scenario in new[]
        {
            (Fixture: "sorcerer-metamagic-complete", Select: "Spell (Sorcerer)", LevelTwoCount: 2, Rejected: Array.Empty<string>()),
            (Fixture: "sorcerer-metamagic-overpick", Select: "Spell (Sorcerer)", LevelTwoCount: 2, Rejected: new[] { "Distant Spell" }),
            (Fixture: "warlock-invocations-complete", Select: "Spellcasting (Warlock)", LevelTwoCount: 1, Rejected: Array.Empty<string>()),
            (Fixture: "warlock-spellcasting-overpick", Select: "Spellcasting (Warlock)", LevelTwoCount: 1, Rejected: new[] { "Chill Touch", "Charm Person" })
        })
        {
            CharacterEvaluationResult result = Evaluate(scenario.Fixture);
            AppliedCharacterChoiceResult[] rejected = result.AppliedChoices.Where(choice => choice.Status != "applied" && choice.Status != "already-applied").ToArray();
            TestAssert.Sequence(scenario.Rejected, rejected.Select(choice => choice.OptionName).ToArray());
            TestAssert.Equal(true, rejected.All(choice => choice.Status == "select-full"));
            foreach (int level in new[] { 1, 2 })
            {
                CharacterSelectResult row = result.AvailableSelects.Single(select => select.SelectName == scenario.Select && select.SelectLevel == level);
                TestAssert.Equal(level == 1 ? 2 : scenario.LevelTwoCount, result.AppliedChoices.Count(choice =>
                    choice.ChoiceRowKey == row.ChoiceRowKey && choice.Status == "applied"));
            }
            TestAssert.Equal(false, result.ComputedCharacter.PendingChoices.Any(choice => choice.SelectType == "Spell"));
            foreach (AppliedCharacterChoiceResult choice in rejected)
                TestAssert.Equal(false, result.DirectSelections.Any(selection => selection.AuroraId == choice.OptionAuroraId));
        }
    }

    public static void CompanionMovement()
    {
        CharacterEvaluationResult result = Evaluate("badger");
        TestAssert.Equal("ID_WOTC_MM25_COMPANION_BADGER", result.DirectSelections.Single().AuroraId);
        TestAssert.Equal(5m, result.ComputedCharacter.EffectRows.Single(row => row.EffectKind == "movement" && row.EffectSubkind == "burrow").NumericValue);
        TestAssert.Equal(20m, result.ComputedCharacter.EffectRows.Single(row => row.EffectKind == "movement" && row.EffectSubkind == "walk").NumericValue);
        TestAssert.Equal(true, result.ComputedCharacter.Traits.Any(trait => trait.Name.Contains("Darkvision 30")));
    }

    private static CharacterEvaluationResult Evaluate(string fixture)
        => AuroraCharacterStateEngine.Evaluate(TestPaths.FirstPartyRegressionDatabasePath, TestPaths.DataPath($"character-state-{fixture}-example.json"));

    private static void AssertApplied(CharacterEvaluationResult result)
        => TestAssert.Sequence(Array.Empty<string>(), result.AppliedChoices
            .Where(choice => choice.Status != "applied" && choice.Status != "already-applied")
            .Select(choice => $"{choice.SelectName}: {choice.OptionName}: {choice.Status}").ToArray());
}
