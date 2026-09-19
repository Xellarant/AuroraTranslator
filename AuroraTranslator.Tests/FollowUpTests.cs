using AuroraTranslator;
using System.Text.Json;

internal static class FollowUpTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal Fixture(string xml)
        {
            string root = Path.Combine(workspace.DirectoryPath, "content");
            Directory.CreateDirectory(Path.Combine(root, "core"));
            File.WriteAllText(Path.Combine(root, "core", "fixture.xml"), "<elements>" + xml + "</elements>");
            AuroraSqliteImporter.ImportFinalized(AuroraTranslator.Program.BuildAuroraImportCatalog(root), TestPaths.SchemaPath, workspace.DatabasePath);
        }
        internal CharacterEvaluationResult Evaluate(AuroraCharacterStateDocument state)
        {
            string file = Path.Combine(workspace.DirectoryPath, "state.json");
            File.WriteAllText(file, JsonSerializer.Serialize(state));
            return AuroraCharacterStateEngine.Evaluate(workspace.DatabasePath, file);
        }
        public void Dispose() => workspace.Dispose();
    }
    private static string Element(string id, string type, string body, string? name = null)
        => $"<element id='{id}' name='{name ?? id}' type='{type}' source='Fixture'>{body}</element>";
    private static string Spell(string id, string list, int level)
        => Element(id, "Spell", $"<supports>{list}</supports><setters><set name='level'>{level}</set><set name='school'>Evocation</set></setters>");
    private static void Options(CharacterEvaluationResult result, string select, params string[] ids)
        => TestAssert.Sequence(ids.Order().ToArray(), result.AvailableSelects.Single(s => s.SelectName == select).Options.Select(o => o.OptionAuroraId!).Order().ToArray());

    internal static void DynamicSupports()
    {
        using var f = new Fixture(Element("ID_OWNER", "Race", """
            <rules><select name='Dynamic' type='Sub Race' supports='(Family || $(extra)), !$(blocked), [level:2]' /></rules>
            """) + Element("ID_GOOD", "Sub Race", "<supports>Family</supports>")
            + Element("ID_OTHER", "Sub Race", "<supports>Alternate</supports>")
            + Element("ID_BLOCKED", "Sub Race", "<supports>Family, Blocked</supports>"));
        var state = new AuroraCharacterStateDocument { Race = new() { AuroraId = "ID_OWNER" },
            MacroValues = new() { ["$(extra)"] = ["Alternate"], ["$(blocked)"] = ["Blocked"] }, NumericValues = new() { ["level"] = 2 } };
        Options(f.Evaluate(state), "Dynamic", "ID_GOOD", "ID_OTHER");
        state.SubRace = new() { AuroraId = "ID_BLOCKED" };
        TestAssert.Equal(1, f.Evaluate(state).ComputedCharacter.PendingChoices.Single(s => s.SelectName == "Dynamic").RemainingCount);
        state.NumericValues["level"] = 1;
        Options(f.Evaluate(state), "Dynamic");
        state.NumericValues["level"] = 2;
        state.MacroValues.Remove("$(blocked)");
        var unresolved = f.Evaluate(state);
        Options(unresolved, "Dynamic");
        TestAssert.Equal(true, unresolved.ComputedCharacter.Warnings.Any(w => w.Message.Contains("$(blocked)")));
    }

    internal static void SpecializedSupports()
    {
        foreach (string type in new[] { "Language", "Proficiency", "Feat" })
        {
            string family = type == "Language" ? "Standard" : type == "Proficiency" ? "Skill" : "Origin";
            using var f = new Fixture(Element("ID_OWNER", "Class Feature", $"<rules><select name='Pick' type='{type}' supports='({family} || Alternate), !Blocked' /></rules>")
                + Element("ID_GOOD", type, $"<supports>{family}</supports>")
                + Element("ID_OTHER", type, "<supports>Alternate</supports>")
                + Element("ID_BLOCKED", type, $"<supports>{family}, Blocked</supports>"));
            Options(f.Evaluate(new() { Elements = [new() { AuroraId = "ID_OWNER" }] }), "Pick", "ID_GOOD", "ID_OTHER");
        }
    }

    internal static void SpellExpressions()
    {
        using var f = new Fixture(Element("ID_OWNER", "Class Feature", "<rules><select name='Spells' type='Spell' supports='((Wizard, 0) || (Cleric, 1)), !ID_BLOCKED' /></rules>")
            + Spell("ID_WIZARD_ZERO", "Wizard", 0) + Spell("ID_WIZARD_ONE", "Wizard", 1)
            + Spell("ID_CLERIC_ZERO", "Cleric", 0) + Spell("ID_CLERIC_ONE", "Cleric", 1) + Spell("ID_BLOCKED", "Cleric", 1));
        Options(f.Evaluate(new() { Elements = [new() { AuroraId = "ID_OWNER" }] }), "Spells", "ID_WIZARD_ZERO", "ID_CLERIC_ONE");
    }

    internal static void DistinctSpellIdentities()
    {
        const string pota = "ID_POTA_SPELL_ERUPTINGEARTH";
        const string xgte = "ID_XGTE_SPELL_ERUPTING_EARTH";
        // Even if the wording and mechanics converge exactly, different Aurora
        // IDs remain separate choices. Source/presentation preference is not identity.
        const string body = "<supports>Sorcerer</supports><description><p>Shared spell text.</p></description><setters><set name='level'>3</set><set name='school'>Transmutation</set></setters>";
        using var f = new Fixture(Element("ID_OWNER", "Class Feature", "<rules><select name='Spells' type='Spell' supports='Sorcerer, 3'/></rules>")
            + Element(pota, "Spell", body, "Erupting Earth").Replace("source='Fixture'", "source='Princes of the Apocalypse'")
            + Element(xgte, "Spell", body, "Erupting Earth").Replace("source='Fixture'", "source='Xanathar&apos;s Guide to Everything'"));
        var state = new AuroraCharacterStateDocument { Elements = [new() { AuroraId = "ID_OWNER" }] };
        var initial = f.Evaluate(state);
        Options(initial, "Spells", pota, xgte);
        foreach (string id in new[] { pota, xgte })
        {
            state.SelectedChoices = [new() { ChoiceRowKey = initial.AvailableSelects.Single().ChoiceRowKey, OptionAuroraId = id }];
            var selected = f.Evaluate(state);
            TestAssert.Equal("applied", selected.AppliedChoices.Single().Status);
            TestAssert.Equal(id, selected.AppliedChoices.Single().OptionAuroraId);
        }
        state.SelectedChoices.Clear();
        state.SourceRestrictions.SourceNames.Add("Xanathar's Guide to Everything");
        Options(f.Evaluate(state), "Spells", pota);
        state.SourceRestrictions.SourceNames.Clear();
        state.SourceRestrictions.SourceNames.Add("Princes of the Apocalypse");
        Options(f.Evaluate(state), "Spells", xgte);
    }

    internal static void SpellOwnership()
    {
        using var f = new Fixture(Element("ID_CLERIC", "Class", "<spellcasting name='Cleric'><list>Cleric</list></spellcasting><rules><grant type='Class Feature' id='ID_SHARED' requirements='ID_ENABLE_CLERIC' /></rules>", "Cleric")
            + Element("ID_WIZARD", "Class", "<spellcasting name='Wizard'><list>Wizard</list></spellcasting><rules><grant type='Class Feature' id='ID_SHARED' /></rules>", "Wizard")
            + Element("ID_SHARED", "Class Feature", "<rules><select name='Spells' type='Spell' supports='$(spellcasting:list), 0' /></rules>")
            + Spell("ID_WIZARD_SPELL", "Wizard", 0) + Spell("ID_CLERIC_SPELL", "Cleric", 0));
        var state = new AuroraCharacterStateDocument { Classes = [new() { AuroraId = "ID_WIZARD", Level = 2 }] };
        Options(f.Evaluate(state), "Spells", "ID_WIZARD_SPELL");
        state.Classes.Add(new() { AuroraId = "ID_CLERIC", Level = 2 });
        Options(f.Evaluate(state), "Spells", "ID_WIZARD_SPELL");
        state.Tokens.Add("ID_ENABLE_CLERIC");
        var ambiguous = f.Evaluate(state);
        Options(ambiguous, "Spells");
        TestAssert.Equal(true, ambiguous.ComputedCharacter.Warnings.Any(w => w.Message.Contains("ID_CLERIC") && w.Message.Contains("ID_WIZARD")));
    }

    internal static void SelectedSpellOwnership()
    {
        using var f = new Fixture(Element("ID_CLERIC", "Class", "<spellcasting name='Cleric'><list>Cleric</list></spellcasting><rules><select name='Feature' type='Class Feature' supports='Shared'/></rules>", "Cleric")
            + Element("ID_WIZARD", "Class", "<spellcasting name='Wizard'><list>Wizard</list></spellcasting><rules><select name='Feature' type='Class Feature' supports='Shared'/></rules>", "Wizard")
            + Element("ID_SHARED", "Class Feature", "<supports>Shared</supports><rules><select name='Spells' type='Spell' supports='$(spellcasting:list), 0'/></rules>")
            + Spell("ID_WIZARD_SPELL", "Wizard", 0) + Spell("ID_CLERIC_SPELL", "Cleric", 0));
        var state = new AuroraCharacterStateDocument { Classes = [new() { AuroraId = "ID_WIZARD" }, new() { AuroraId = "ID_CLERIC" }] };
        var selector = f.Evaluate(state).AvailableSelects.Single(s => s.OwnerName == "Wizard" && s.SelectName == "Feature");
        state.SelectedChoices.Add(new() { ChoiceRowKey = selector.ChoiceRowKey, OptionAuroraId = "ID_SHARED" });
        Options(f.Evaluate(state), "Spells", "ID_WIZARD_SPELL");
    }

    internal static void SpellSlotBindings()
    {
        using var f = new Fixture(Element("ID_WIZARD", "Class", "<rules><grant type='Class Feature' id='ID_CASTING'/></rules>", "Wizard")
            + Element("ID_INACTIVE", "Class", "<rules><stat name='wizard:spellcasting:slots:9' value='1'/></rules>", "Unselected caster")
            + Element("ID_CASTING", "Class Feature", """
                <spellcasting name='Wizard'><list>Wizard,(Abjuration||Evocation)</list></spellcasting>
                <rules><stat name='wizard:spellcasting:slots:1' value='2'/>
                <select name='Spells' type='Spell' supports='$(spellcasting:list), (0||$(spellcasting:slots))'/></rules>
                """)
            + Spell("ID_CANTRIP", "Wizard", 0) + Spell("ID_FIRST", "Wizard", 1) + Spell("ID_SECOND", "Wizard", 2)
            + Spell("ID_OTHER", "Cleric", 1));
        Options(f.Evaluate(new() { Classes = [new() { AuroraId = "ID_WIZARD", Level = 2 }] }), "Spells", "ID_CANTRIP", "ID_FIRST");
    }
}
