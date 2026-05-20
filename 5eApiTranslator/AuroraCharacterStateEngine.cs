using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AuroraTranslator
{
    internal sealed class AuroraCharacterStateDocument
    {
        public List<AuroraCharacterStateSelection> Classes { get; set; } = new();
        public List<AuroraCharacterStateSelection> Archetypes { get; set; } = new();
        public AuroraCharacterStateSelection Race { get; set; }
        public AuroraCharacterStateSelection SubRace { get; set; }
        public List<AuroraCharacterStateSelection> RaceVariants { get; set; } = new();
        public AuroraCharacterStateSelection Background { get; set; }
        public List<AuroraCharacterStateSelection> Feats { get; set; } = new();
        public List<AuroraCharacterStateSelection> Proficiencies { get; set; } = new();
        public List<AuroraCharacterStateSelection> Languages { get; set; } = new();
        public List<AuroraCharacterStateSelection> Elements { get; set; } = new();
        public List<AuroraCharacterStateChoice> SelectedChoices { get; set; } = new();
        public List<string> Tokens { get; set; } = new();
        public Dictionary<string, decimal> NumericValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ScalarValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> MacroValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static AuroraCharacterStateDocument Load(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("A character state JSON path is required.", nameof(jsonPath));
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Character state JSON not found: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<AuroraCharacterStateDocument>(
                       json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AuroraCharacterStateDocument();
        }
    }

    internal sealed class AuroraCharacterStateSelection
    {
        public string AuroraId { get; set; }
        public string Name { get; set; }
        public string PackageKey { get; set; }
        public int? Level { get; set; }
    }

    internal sealed class AuroraCharacterStateChoice
    {
        public int? SelectId { get; set; }
        public string OwnerName { get; set; }
        public string OwnerTypeName { get; set; }
        public string SelectName { get; set; }
        public string SelectType { get; set; }
        public int? OptionElementId { get; set; }
        public string OptionAuroraId { get; set; }
        public string OptionName { get; set; }
        public string OptionText { get; set; }
        public int? FollowUpOptionElementId { get; set; }
        public string FollowUpOptionAuroraId { get; set; }
        public string FollowUpOptionName { get; set; }
        public string FollowUpOptionText { get; set; }
    }

    internal sealed record ResolvedCharacterElement(
        int ElementId,
        string AuroraId,
        string Name,
        string TypeName,
        string PackageKey,
        string SourcePath,
        int? Level);

    internal sealed record ActiveCharacterFeature(
        int ElementId,
        string AuroraId,
        string Name,
        string TypeName,
        string PackageKey,
        string SourcePath,
        int UnlockLevel,
        string OwnerName,
        string OwnerTypeName);

    internal sealed record ActiveGrantResult(
        int GrantId,
        string OwnerName,
        string OwnerTypeName,
        string GrantType,
        int? GrantLevel,
        string SpellcastingName,
        bool? IsPrepared,
        string RequirementsText,
        int? TargetElementId,
        string TargetAuroraId,
        string TargetName,
        string TargetTypeName,
        string TargetPackageKey,
        string TargetSemanticKey,
        string TargetSemanticKind,
        string TargetSemanticName);

    internal sealed record CharacterSelectOptionResult(
        string OptionKind,
        int? OptionElementId,
        string OptionAuroraId,
        string OptionName,
        string OptionTypeName,
        string OptionPackageKey,
        string OptionText,
        bool IsAvailable,
        bool IsAlreadyOwned,
        bool IsChosenForSelect,
        string RequirementText,
        string FollowUpKind = null,
        IReadOnlyList<CharacterSelectOptionResult> FollowUpOptions = null);

    internal sealed record CharacterSelectResult(
        int SelectId,
        string OwnerName,
        string OwnerTypeName,
        string OwnerPackageKey,
        string SelectName,
        string SelectType,
        string SelectPolicy,
        string ChoiceFamily,
        string SupportsText,
        int? SelectLevel,
        int NumberToChoose,
        bool IsOptional,
        string RequirementsText,
        IReadOnlyList<CharacterSelectOptionResult> Options);

    internal sealed record AppliedCharacterChoiceResult(
        int ChoiceIndex,
        int? SelectId,
        string OwnerName,
        string OwnerTypeName,
        string SelectName,
        string SelectType,
        string ChoiceFamily,
        string OptionName,
        string OptionAuroraId,
        string FollowUpOptionName,
        string FollowUpOptionAuroraId,
        string Status,
        string Message);

    internal sealed record CharacterProvenanceEntry(
        string Category,
        string Key,
        string SourceKind,
        string OwnerName,
        string OwnerTypeName,
        string PackageKey,
        string ElementAuroraId,
        string ElementName,
        string Detail);

    internal sealed record ComputedAbilityScoreResult(
        string AbilityKey,
        string AbilityName,
        decimal BaseValue,
        decimal ModifierTotal,
        decimal FinalValue,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record ComputedCharacterItemResult(
        string Category,
        string Key,
        string Name,
        string TypeName,
        string PackageKey,
        bool IsDirectSelection,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record ComputedGrantedSpellResult(
        string SpellKey,
        string SpellAuroraId,
        string SpellName,
        string SpellPackageKey,
        string SpellcastingName,
        bool? IsPrepared,
        int? GrantLevel,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record ComputedSpellcastingProfileResult(
        string ProfileKey,
        string SpellcastingName,
        int GrantedSpellCount,
        int PreparedSpellCount,
        int UnpreparedSpellCount,
        IReadOnlyList<string> SpellKeys,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record ComputedEffectRowResult(
        string EffectKind,
        string EffectSubkind,
        string EffectKey,
        string DisplayName,
        string ValueText,
        decimal? NumericValue,
        string PackageKey,
        bool IsDirectSelection,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record PendingCharacterChoiceResult(
        int SelectId,
        string OwnerName,
        string OwnerTypeName,
        string OwnerPackageKey,
        string SelectName,
        string SelectType,
        string SelectPolicy,
        string ChoiceFamily,
        int NumberToChoose,
        int ChosenCount,
        int AlreadyOwnedCount,
        int RemainingCount,
        int AvailableOptionCount,
        bool IsOptional,
        bool IsBlocking);

    internal sealed record CharacterWarningResult(
        string WarningKind,
        string Severity,
        string Message,
        string OwnerName,
        string OwnerTypeName,
        string SelectName);

    internal sealed record ParsedMovementResult(
        string Kind,
        string Label,
        string ValueText,
        string SourceText);

    internal sealed record ComputedCharacterResult(
        IReadOnlyList<ComputedAbilityScoreResult> AbilityScores,
        IReadOnlyList<ComputedCharacterItemResult> Proficiencies,
        IReadOnlyList<ComputedCharacterItemResult> Languages,
        IReadOnlyList<ComputedCharacterItemResult> Feats,
        IReadOnlyList<ComputedCharacterItemResult> Features,
        IReadOnlyList<ComputedGrantedSpellResult> GrantedSpells,
        IReadOnlyList<ComputedSpellcastingProfileResult> SpellcastingProfiles,
        IReadOnlyList<ComputedEffectRowResult> EffectRows,
        IReadOnlyList<ComputedCharacterItemResult> ChoiceSelections,
        IReadOnlyList<ComputedCharacterItemResult> Traits,
        IReadOnlyList<PendingCharacterChoiceResult> PendingChoices,
        IReadOnlyList<CharacterWarningResult> Warnings,
        IReadOnlyList<CharacterProvenanceEntry> Provenance);

    internal sealed record CharacterEvaluationResult(
        IReadOnlyList<ResolvedCharacterElement> DirectSelections,
        IReadOnlyList<ActiveCharacterFeature> ActiveFeatures,
        IReadOnlyList<ActiveGrantResult> ActiveGrants,
        IReadOnlyList<CharacterSelectResult> AvailableSelects,
        AuroraExpressionEvaluationContext EvaluationContext,
        IReadOnlyList<AppliedCharacterChoiceResult> AppliedChoices,
        ComputedCharacterResult ComputedCharacter);

    internal static class AuroraCharacterStateEngine
    {
        public static CharacterEvaluationResult Evaluate(string sqlitePath, string stateJsonPath)
        {
            if (string.IsNullOrWhiteSpace(sqlitePath))
                throw new ArgumentException("A SQLite path is required.", nameof(sqlitePath));
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");

            AuroraCharacterStateDocument document = AuroraCharacterStateDocument.Load(stateJsonPath);

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();

            AuroraCharacterStateDocument workingDocument = CloneDocument(document);
            var appliedChoiceResults = new Dictionary<int, AppliedCharacterChoiceResult>();
            var completedChoices = new HashSet<int>();
            CharacterEvaluationResult current = EvaluateCore(connection, workingDocument, Array.Empty<AppliedCharacterChoiceResult>());

            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool anyApplied = false;

                if (ApplyImplicitFeaturePickSelections(connection, workingDocument, current))
                {
                    anyApplied = true;
                    current = EvaluateCore(connection, workingDocument, appliedChoiceResults.Values.OrderBy(x => x.ChoiceIndex).ToList());
                }

                for (int choiceIndex = 0; choiceIndex < workingDocument.SelectedChoices.Count; choiceIndex++)
                {
                    if (completedChoices.Contains(choiceIndex))
                        continue;

                    AuroraCharacterStateChoice choice = workingDocument.SelectedChoices[choiceIndex];
                    AppliedCharacterChoiceResult result = ResolveAndApplySelectedChoice(
                        connection,
                        workingDocument,
                        current,
                        choiceIndex,
                        choice);

                    appliedChoiceResults[choiceIndex] = result;
                    if (string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase))
                        anyApplied = true;
                    if (string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(result.Status, "already-applied", StringComparison.OrdinalIgnoreCase))
                    {
                        completedChoices.Add(choiceIndex);
                    }
                }

                if (!anyApplied)
                    break;

                current = EvaluateCore(connection, workingDocument, appliedChoiceResults.Values.OrderBy(x => x.ChoiceIndex).ToList());
            }

            CharacterEvaluationResult final = EvaluateCore(connection, workingDocument, appliedChoiceResults.Values.OrderBy(x => x.ChoiceIndex).ToList());
            for (int choiceIndex = 0; choiceIndex < workingDocument.SelectedChoices.Count; choiceIndex++)
            {
                if (!appliedChoiceResults.ContainsKey(choiceIndex))
                {
                    appliedChoiceResults[choiceIndex] = ResolveAndApplySelectedChoice(
                        connection,
                        workingDocument,
                        final,
                        choiceIndex,
                        workingDocument.SelectedChoices[choiceIndex],
                        applyChanges: false);
                }
            }

            IReadOnlyList<AppliedCharacterChoiceResult> finalizedChoices = appliedChoiceResults.Values
                .OrderBy(x => x.ChoiceIndex)
                .ToList();
            ComputedCharacterResult computedCharacter = BuildComputedCharacter(connection, document, workingDocument, final with
            {
                AppliedChoices = finalizedChoices
            });

            return final with
            {
                AppliedChoices = finalizedChoices,
                ComputedCharacter = computedCharacter
            };
        }

        private static CharacterEvaluationResult EvaluateCore(
            SqliteConnection connection,
            AuroraCharacterStateDocument document,
            IReadOnlyList<AppliedCharacterChoiceResult> appliedChoices)
        {
            var directSelections = ResolveDirectSelections(connection, document);
            var activeFeatures = LoadActiveFeatures(connection, directSelections);
            var evaluationContext = BuildInitialContext(document, directSelections, activeFeatures, connection);
            Dictionary<int, int> ownerLevels = BuildOwnerLevelMap(directSelections, activeFeatures, connection);
            var activeGrants = LoadActiveGrants(connection, ownerLevels, evaluationContext);

            for (int iteration = 0; iteration < 3; iteration++)
            {
                int tokenCountBefore = evaluationContext.Tokens.Count;
                int macroCountBefore = evaluationContext.MacroValues.Sum(x => x.Value.Count);
                AddGrantTokensToContext(evaluationContext, activeGrants, connection);
                if (evaluationContext.Tokens.Count == tokenCountBefore
                    && evaluationContext.MacroValues.Sum(x => x.Value.Count) == macroCountBefore)
                {
                    break;
                }

                activeGrants = LoadActiveGrants(connection, ownerLevels, evaluationContext);
            }

            var availableSelects = LoadAvailableSelects(connection, ownerLevels, evaluationContext);
            return new CharacterEvaluationResult(
                directSelections,
                activeFeatures,
                activeGrants,
                availableSelects,
                evaluationContext,
                appliedChoices,
                null);
        }

        private static List<ResolvedCharacterElement> ResolveDirectSelections(
            SqliteConnection connection,
            AuroraCharacterStateDocument document)
        {
            var resolved = new List<ResolvedCharacterElement>();

            resolved.AddRange(ResolveSelections(connection, "Class", document.Classes));
            resolved.AddRange(ResolveSelections(connection, "Archetype", document.Archetypes));
            resolved.AddRange(ResolveSelections(connection, "Background", Wrap(document.Background)));
            resolved.AddRange(ResolveSelections(connection, "Race", Wrap(document.Race)));
            resolved.AddRange(ResolveSelections(connection, "Sub Race", Wrap(document.SubRace)));
            resolved.AddRange(ResolveSelections(connection, "Race Variant", document.RaceVariants));
            resolved.AddRange(ResolveSelections(connection, "Feat", document.Feats));
            resolved.AddRange(ResolveSelections(connection, "Proficiency", document.Proficiencies));
            resolved.AddRange(ResolveSelections(connection, "Language", document.Languages));
            resolved.AddRange(ResolveSelections(connection, null, document.Elements));

            return resolved
                .GroupBy(x => x.ElementId)
                .Select(x => x.First())
                .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PackageKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<AuroraCharacterStateSelection> Wrap(AuroraCharacterStateSelection selection)
        {
            if (selection == null)
                yield break;

            yield return selection;
        }

        private static AuroraCharacterStateDocument CloneDocument(AuroraCharacterStateDocument source)
        {
            return new AuroraCharacterStateDocument
            {
                Classes = CloneSelections(source.Classes),
                Archetypes = CloneSelections(source.Archetypes),
                Race = CloneSelection(source.Race),
                SubRace = CloneSelection(source.SubRace),
                RaceVariants = CloneSelections(source.RaceVariants),
                Background = CloneSelection(source.Background),
                Feats = CloneSelections(source.Feats),
                Proficiencies = CloneSelections(source.Proficiencies),
                Languages = CloneSelections(source.Languages),
                Elements = CloneSelections(source.Elements),
                SelectedChoices = source.SelectedChoices?
                    .Select(CloneChoice)
                    .ToList()
                    ?? new List<AuroraCharacterStateChoice>(),
                Tokens = source.Tokens?.ToList() ?? new List<string>(),
                NumericValues = source.NumericValues?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
                ScalarValues = source.ScalarValues?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                MacroValues = source.MacroValues?.ToDictionary(
                        x => x.Key,
                        x => x.Value?.ToList() ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static List<AuroraCharacterStateSelection> CloneSelections(List<AuroraCharacterStateSelection> selections)
        {
            return selections?.Select(CloneSelection).Where(x => x != null).ToList()
                   ?? new List<AuroraCharacterStateSelection>();
        }

        private static AuroraCharacterStateSelection CloneSelection(AuroraCharacterStateSelection selection)
        {
            if (selection == null)
                return null;

            return new AuroraCharacterStateSelection
            {
                AuroraId = selection.AuroraId,
                Name = selection.Name,
                PackageKey = selection.PackageKey,
                Level = selection.Level
            };
        }

        private static AuroraCharacterStateChoice CloneChoice(AuroraCharacterStateChoice choice)
        {
            if (choice == null)
                return null;

            return new AuroraCharacterStateChoice
            {
                SelectId = choice.SelectId,
                OwnerName = choice.OwnerName,
                OwnerTypeName = choice.OwnerTypeName,
                SelectName = choice.SelectName,
                SelectType = choice.SelectType,
                OptionElementId = choice.OptionElementId,
                OptionAuroraId = choice.OptionAuroraId,
                OptionName = choice.OptionName,
                OptionText = choice.OptionText,
                FollowUpOptionElementId = choice.FollowUpOptionElementId,
                FollowUpOptionAuroraId = choice.FollowUpOptionAuroraId,
                FollowUpOptionName = choice.FollowUpOptionName,
                FollowUpOptionText = choice.FollowUpOptionText
            };
        }

        private static List<ResolvedCharacterElement> ResolveSelections(
            SqliteConnection connection,
            string typeName,
            IEnumerable<AuroraCharacterStateSelection> selections)
        {
            var resolved = new List<ResolvedCharacterElement>();

            foreach (AuroraCharacterStateSelection selection in selections ?? Enumerable.Empty<AuroraCharacterStateSelection>())
            {
                if (selection == null)
                    continue;

                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    et.type_name,
    rec.package_key,
    sf.relative_path
FROM elements AS e
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = e.element_id
JOIN source_files AS sf
    ON sf.source_file_id = e.source_file_id
WHERE
    (
        ($type_name IS NULL)
        OR et.type_name = $type_name
    )
  AND
    (
        ($aurora_id <> '' AND e.aurora_id = $aurora_id)
        OR
        (
            $name <> ''
            AND e.name = $name
            AND ($package_key = '' OR rec.package_key = $package_key)
        )
    )
ORDER BY rec.package_key ASC, e.name ASC;";

                command.Parameters.AddWithValue("$type_name", (object)typeName ?? DBNull.Value);
                command.Parameters.AddWithValue("$aurora_id", selection.AuroraId?.Trim() ?? string.Empty);
                command.Parameters.AddWithValue("$name", selection.Name?.Trim() ?? string.Empty);
                command.Parameters.AddWithValue("$package_key", selection.PackageKey?.Trim() ?? string.Empty);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    resolved.Add(new ResolvedCharacterElement(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        selection.Level));
                }
            }

            return resolved;
        }

        private static AppliedCharacterChoiceResult ResolveAndApplySelectedChoice(
            SqliteConnection connection,
            AuroraCharacterStateDocument workingDocument,
            CharacterEvaluationResult evaluation,
            int choiceIndex,
            AuroraCharacterStateChoice choice,
            bool applyChanges = true)
        {
            CharacterSelectResult select = MatchSelect(evaluation.AvailableSelects, choice);
            if (select == null)
            {
                return BuildChoiceResult(choiceIndex, choice, null, null, null, "select-not-available", "The targeted select is not currently available.");
            }

            CharacterSelectOptionResult option = MatchOption(select.Options, choice.OptionElementId, choice.OptionAuroraId, choice.OptionName, choice.OptionText);
            if (option == null)
            {
                return BuildChoiceResult(choiceIndex, choice, select, null, null, "option-not-found", "The targeted option was not found in the available option pool.");
            }

            if (!option.IsAvailable)
            {
                return BuildChoiceResult(choiceIndex, choice, select, option, null, "option-unavailable", "The targeted option is present but not currently available.");
            }

            CharacterSelectOptionResult followUp = null;
            bool requestedFollowUp = choice.FollowUpOptionElementId.HasValue
                                     || !string.IsNullOrWhiteSpace(choice.FollowUpOptionAuroraId)
                                     || !string.IsNullOrWhiteSpace(choice.FollowUpOptionName)
                                     || !string.IsNullOrWhiteSpace(choice.FollowUpOptionText);

            if (requestedFollowUp)
            {
                followUp = MatchOption(
                    option.FollowUpOptions ?? Array.Empty<CharacterSelectOptionResult>(),
                    choice.FollowUpOptionElementId,
                    choice.FollowUpOptionAuroraId,
                    choice.FollowUpOptionName,
                    choice.FollowUpOptionText);

                if (followUp == null)
                {
                    return BuildChoiceResult(choiceIndex, choice, select, option, null, "follow-up-not-found", "The requested follow-up option was not found.");
                }

                if (!followUp.IsAvailable)
                {
                    return BuildChoiceResult(choiceIndex, choice, select, option, followUp, "follow-up-unavailable", "The requested follow-up option is present but not currently available.");
                }
            }
            else if (RequiresFollowUpToApply(option))
            {
                return BuildChoiceResult(choiceIndex, choice, select, option, null, "follow-up-required", "This option requires a follow-up choice before it can be applied.");
            }

            if (!applyChanges)
            {
                return BuildChoiceResult(choiceIndex, choice, select, option, followUp, "ready", "The choice is available and ready to apply.");
            }

            bool changed = false;
            string message = "Choice already reflected in the current state.";

            if (followUp != null)
            {
                changed = ApplyOptionToDocument(connection, workingDocument, select, followUp);
                message = changed
                    ? "Applied selected follow-up choice."
                    : "Follow-up choice was already reflected in the current state.";
            }
            else
            {
                changed = ApplyOptionToDocument(connection, workingDocument, select, option);
                message = changed
                    ? "Applied selected choice."
                    : "Choice was already reflected in the current state.";
            }

            return BuildChoiceResult(choiceIndex, choice, select, option, followUp, changed ? "applied" : "already-applied", message);
        }

        private static CharacterSelectResult MatchSelect(
            IReadOnlyList<CharacterSelectResult> availableSelects,
            AuroraCharacterStateChoice choice)
        {
            IEnumerable<CharacterSelectResult> candidates = availableSelects;

            if (choice.SelectId.HasValue)
            {
                CharacterSelectResult select = candidates.FirstOrDefault(x => x.SelectId == choice.SelectId.Value);
                if (select != null)
                    return select;
            }

            candidates = candidates.Where(select =>
                (string.IsNullOrWhiteSpace(choice.OwnerName)
                 || string.Equals(select.OwnerName, choice.OwnerName, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(choice.OwnerTypeName)
                    || string.Equals(select.OwnerTypeName, choice.OwnerTypeName, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(choice.SelectName)
                    || string.Equals(select.SelectName, choice.SelectName, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(choice.SelectType)
                    || string.Equals(select.SelectType, choice.SelectType, StringComparison.OrdinalIgnoreCase)));

            return candidates
                .OrderBy(select => select.SelectId)
                .FirstOrDefault();
        }

        private static CharacterSelectOptionResult MatchOption(
            IEnumerable<CharacterSelectOptionResult> options,
            int? optionElementId,
            string optionAuroraId,
            string optionName,
            string optionText)
        {
            IEnumerable<CharacterSelectOptionResult> candidates = options ?? Enumerable.Empty<CharacterSelectOptionResult>();

            if (optionElementId.HasValue)
            {
                CharacterSelectOptionResult option = candidates.FirstOrDefault(x => x.OptionElementId == optionElementId.Value);
                if (option != null)
                    return option;
            }

            if (!string.IsNullOrWhiteSpace(optionAuroraId))
            {
                CharacterSelectOptionResult option = candidates.FirstOrDefault(x =>
                    string.Equals(x.OptionAuroraId, optionAuroraId, StringComparison.OrdinalIgnoreCase));
                if (option != null)
                    return option;
            }

            if (!string.IsNullOrWhiteSpace(optionName))
            {
                CharacterSelectOptionResult option = candidates.FirstOrDefault(x =>
                    string.Equals(x.OptionName, optionName, StringComparison.OrdinalIgnoreCase));
                if (option != null)
                    return option;
            }

            if (!string.IsNullOrWhiteSpace(optionText))
            {
                CharacterSelectOptionResult option = candidates.FirstOrDefault(x =>
                    string.Equals(x.OptionText, optionText, StringComparison.OrdinalIgnoreCase));
                if (option != null)
                    return option;
            }

            return null;
        }

        private static bool RequiresFollowUpToApply(CharacterSelectOptionResult option)
        {
            if ((option?.FollowUpOptions?.Count ?? 0) == 0)
                return false;

            return string.Equals(option.OptionKind, "semantic", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(option.FollowUpKind, "ability-score-improvement", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(option.FollowUpKind, "feat-selection", StringComparison.OrdinalIgnoreCase);
        }

        private static AppliedCharacterChoiceResult BuildChoiceResult(
            int choiceIndex,
            AuroraCharacterStateChoice choice,
            CharacterSelectResult select,
            CharacterSelectOptionResult option,
            CharacterSelectOptionResult followUp,
            string status,
            string message)
        {
            return new AppliedCharacterChoiceResult(
                choiceIndex,
                select?.SelectId ?? choice.SelectId,
                select?.OwnerName ?? choice.OwnerName,
                select?.OwnerTypeName ?? choice.OwnerTypeName,
                select?.SelectName ?? choice.SelectName,
                select?.SelectType ?? choice.SelectType,
                select?.ChoiceFamily ?? ClassifyChoiceFamily(choice.SelectType, null, choice.SelectName, null),
                option?.OptionName ?? option?.OptionText ?? choice.OptionName ?? choice.OptionText,
                option?.OptionAuroraId ?? choice.OptionAuroraId,
                followUp?.OptionName ?? followUp?.OptionText ?? choice.FollowUpOptionName ?? choice.FollowUpOptionText,
                followUp?.OptionAuroraId ?? choice.FollowUpOptionAuroraId,
                status,
                message);
        }

        private static bool ApplyImplicitFeaturePickSelections(
            SqliteConnection connection,
            AuroraCharacterStateDocument document,
            CharacterEvaluationResult current)
        {
            if (current?.AvailableSelects == null || current.AvailableSelects.Count == 0)
                return false;

            bool changed = false;
            foreach (CharacterSelectResult select in current.AvailableSelects)
            {
                if (!ShouldImplicitlyApplyFeaturePick(select))
                    continue;

                CharacterSelectOptionResult option = select.Options.FirstOrDefault(x => x.IsAvailable);
                if (option == null)
                    continue;

                bool applied = ApplyOptionToDocument(connection, document, select, option);
                bool appliedReplacementTokens = ApplyReplacementTokensToDocument(document, select, option);
                changed = applied || appliedReplacementTokens || changed;
            }

            return changed;
        }

        private static bool ShouldImplicitlyApplyFeaturePick(CharacterSelectResult select)
        {
            if (select == null
                || select.IsOptional
                || select.NumberToChoose != 1
                || !string.Equals(select.ChoiceFamily, "feature-pick", StringComparison.OrdinalIgnoreCase)
                || RequiresFollowUpToApply(select.Options?.FirstOrDefault()))
            {
                return false;
            }

            List<CharacterSelectOptionResult> availableOptions = select.Options?
                .Where(x => x.IsAvailable)
                .ToList();

            return availableOptions?.Count == 1 && availableOptions[0].OptionElementId.HasValue;
        }

        private static bool ApplyReplacementTokensToDocument(
            AuroraCharacterStateDocument document,
            CharacterSelectResult select,
            CharacterSelectOptionResult option)
        {
            if (document == null)
                return false;

            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddReplacementTokens(tokens, select?.RequirementsText);
            AddReplacementTokens(tokens, option?.RequirementText);

            bool changed = false;
            foreach (string token in tokens)
            {
                if (document.Tokens.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                    continue;

                document.Tokens.Add(token);
                changed = true;
            }

            return changed;
        }

        private static void AddReplacementTokens(ISet<string> sink, string requirementsText)
        {
            if (sink == null || string.IsNullOrWhiteSpace(requirementsText))
                return;

            foreach (Match match in Regex.Matches(
                         requirementsText,
                         @"ID_INTERNAL_[A-Z0-9_]*FEATURE_REPLACEMENT[A-Z0-9_]*",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string token = match.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    sink.Add(token);
            }
        }

        private static bool ApplyOptionToDocument(
            SqliteConnection connection,
            AuroraCharacterStateDocument document,
            CharacterSelectResult select,
            CharacterSelectOptionResult option)
        {
            if (option == null)
                return false;

            if (string.Equals(option.OptionKind, "semantic", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(option.OptionAuroraId, "SEMANTIC_ASI", StringComparison.OrdinalIgnoreCase)
                    || option.OptionAuroraId?.StartsWith("ASI_", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ApplyAsiOptionToDocument(document, option);
                }

                return false;
            }

            if (!option.OptionElementId.HasValue)
                return ApplyTextChoiceToDocument(document, select, option);

            var selection = new AuroraCharacterStateSelection
            {
                AuroraId = option.OptionAuroraId,
                Name = option.OptionName,
                PackageKey = option.OptionPackageKey
            };
            string storedChoiceValue = !string.IsNullOrWhiteSpace(option.OptionAuroraId)
                ? option.OptionAuroraId
                : !string.IsNullOrWhiteSpace(option.OptionName)
                    ? option.OptionName
                    : option.OptionText;
            bool storedChoice = StoreChoiceValue(document, select, storedChoiceValue);

            string optionTypeName = option.OptionTypeName?.Trim();
            if (string.Equals(optionTypeName, "Archetype", StringComparison.OrdinalIgnoreCase))
                return AddSelection(document.Archetypes, selection) || storedChoice;
            if (string.Equals(optionTypeName, "Race Variant", StringComparison.OrdinalIgnoreCase))
                return AddSelection(document.RaceVariants, selection) || storedChoice;
            if (string.Equals(optionTypeName, "Feat", StringComparison.OrdinalIgnoreCase))
                return AddSelection(document.Feats, selection) || storedChoice;
            if (string.Equals(optionTypeName, "Language", StringComparison.OrdinalIgnoreCase))
                return AddSelection(document.Languages, selection) || storedChoice;
            if (string.Equals(optionTypeName, "Proficiency", StringComparison.OrdinalIgnoreCase))
                return AddSelection(document.Proficiencies, selection) || storedChoice;
            if (string.Equals(optionTypeName, "Sub Race", StringComparison.OrdinalIgnoreCase))
            {
                if (MatchesSelection(document.SubRace, selection))
                    return storedChoice;
                document.SubRace = selection;
                return true;
            }
            if (string.Equals(optionTypeName, "Background", StringComparison.OrdinalIgnoreCase))
            {
                if (MatchesSelection(document.Background, selection))
                    return storedChoice;
                document.Background = selection;
                return true;
            }
            if (string.Equals(optionTypeName, "Race", StringComparison.OrdinalIgnoreCase))
            {
                if (MatchesSelection(document.Race, selection))
                    return storedChoice;
                document.Race = selection;
                return true;
            }

            return AddSelection(document.Elements, selection) || storedChoice;
        }

        private static bool ApplyAsiOptionToDocument(AuroraCharacterStateDocument document, CharacterSelectOptionResult option)
        {
            string payload = option.OptionText;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            using JsonDocument jsonDocument = JsonDocument.Parse(payload);
            string mode = jsonDocument.RootElement.TryGetProperty("mode", out JsonElement modeElement)
                ? modeElement.GetString()
                : null;
            JsonElement abilitiesElement = jsonDocument.RootElement.GetProperty("abilities");
            List<string> abilities = abilitiesElement.EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (abilities.Count == 0)
                return false;

            decimal delta = string.Equals(mode, "plus2", StringComparison.OrdinalIgnoreCase) ? 2m : 1m;
            bool changed = false;
            foreach (string ability in abilities)
            {
                decimal current = document.NumericValues.TryGetValue(ability, out decimal value) ? value : 0m;
                decimal updated = current + delta;
                if (updated != current)
                {
                    document.NumericValues[ability] = updated;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ApplyTextChoiceToDocument(
            AuroraCharacterStateDocument document,
            CharacterSelectResult select,
            CharacterSelectOptionResult option)
        {
            if (select == null)
                return false;

            string selectedValue = option.OptionText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedValue))
                selectedValue = option.OptionName?.Trim();

            if (string.IsNullOrWhiteSpace(selectedValue))
                return false;

            return StoreChoiceValue(document, select, selectedValue);
        }

        private static bool AddSelection(List<AuroraCharacterStateSelection> selections, AuroraCharacterStateSelection candidate)
        {
            selections ??= new List<AuroraCharacterStateSelection>();
            if (selections.Any(existing => MatchesSelection(existing, candidate)))
                return false;

            selections.Add(candidate);
            return true;
        }

        private static bool MatchesSelection(AuroraCharacterStateSelection existing, AuroraCharacterStateSelection candidate)
        {
            if (existing == null || candidate == null)
                return false;

            if (!string.IsNullOrWhiteSpace(existing.AuroraId) && !string.IsNullOrWhiteSpace(candidate.AuroraId))
                return string.Equals(existing.AuroraId, candidate.AuroraId, StringComparison.OrdinalIgnoreCase);

            return string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(existing.PackageKey, candidate.PackageKey, StringComparison.OrdinalIgnoreCase);
        }

        private static List<ActiveCharacterFeature> LoadActiveFeatures(
            SqliteConnection connection,
            IReadOnlyList<ResolvedCharacterElement> directSelections)
        {
            var features = new List<ActiveCharacterFeature>();

            foreach (ResolvedCharacterElement classSelection in directSelections.Where(x => string.Equals(x.TypeName, "Class", StringComparison.OrdinalIgnoreCase)))
            {
                int classLevel = classSelection.Level.GetValueOrDefault(1);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    feature_element_id,
    feature_aurora_id,
    feature_name,
    feature_type_name,
    feature_package_key,
    feature_source_path,
    unlock_level
FROM v_class_feature_progression
WHERE class_element_id = $class_element_id
  AND unlock_level <= $class_level
ORDER BY unlock_level ASC, feature_name ASC;";
                command.Parameters.AddWithValue("$class_element_id", classSelection.ElementId);
                command.Parameters.AddWithValue("$class_level", classLevel);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    features.Add(new ActiveCharacterFeature(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetInt32(6),
                        classSelection.Name,
                        classSelection.TypeName));
                }
            }

            foreach (ResolvedCharacterElement archetypeSelection in directSelections.Where(x => string.Equals(x.TypeName, "Archetype", StringComparison.OrdinalIgnoreCase)))
            {
                int archetypeLevel = ResolveArchetypeLevel(archetypeSelection, directSelections, connection);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    feature_element_id,
    feature_aurora_id,
    feature_name,
    feature_type_name,
    feature_package_key,
    feature_source_path,
    unlock_level
FROM v_archetype_feature_progression
WHERE archetype_element_id = $archetype_element_id
  AND unlock_level <= $archetype_level
ORDER BY unlock_level ASC, feature_name ASC;";
                command.Parameters.AddWithValue("$archetype_element_id", archetypeSelection.ElementId);
                command.Parameters.AddWithValue("$archetype_level", archetypeLevel);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    features.Add(new ActiveCharacterFeature(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetInt32(6),
                        archetypeSelection.Name,
                        archetypeSelection.TypeName));
                }
            }

            foreach (ResolvedCharacterElement parentSelection in directSelections.Where(x =>
                         x.TypeName is "Background" or "Race" or "Sub Race" or "Race Variant" or "Feat"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    feature_element.element_id,
    feature_element.aurora_id,
    feature_element.name,
    feature_type.type_name,
    rec.package_key,
    sf.relative_path,
    COALESCE(f.min_level, 1) AS unlock_level
FROM features AS f
JOIN elements AS feature_element
    ON feature_element.element_id = f.element_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = feature_element.element_id
JOIN source_files AS sf
    ON sf.source_file_id = feature_element.source_file_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
WHERE f.parent_element_id = $parent_element_id
  AND NOT EXISTS
  (
      SELECT 1
      FROM v_selectable_options AS selectable
      WHERE selectable.owner_element_id = $parent_element_id
        AND selectable.option_kind = 'element'
        AND
        (
            selectable.option_element_id = feature_element.element_id
            OR
            (
                selectable.option_name IS NOT NULL
                AND selectable.option_name = feature_element.name
            )
        )
  )
ORDER BY unlock_level ASC, feature_element.name ASC;";
                command.Parameters.AddWithValue("$parent_element_id", parentSelection.ElementId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    features.Add(new ActiveCharacterFeature(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetInt32(6),
                        parentSelection.Name,
                        parentSelection.TypeName));
                }
            }

            return features
                .GroupBy(x => x.ElementId)
                .Select(x => x.First())
                .OrderBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UnlockLevel)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ResolveArchetypeLevel(
            ResolvedCharacterElement archetypeSelection,
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            SqliteConnection connection)
        {
            if (archetypeSelection.Level.HasValue)
                return archetypeSelection.Level.Value;

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT parent_class_element_id
FROM archetypes
WHERE element_id = $element_id;";
            command.Parameters.AddWithValue("$element_id", archetypeSelection.ElementId);
            object result = command.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return 1;

            int parentClassElementId = Convert.ToInt32(result);
            ResolvedCharacterElement classSelection = directSelections.FirstOrDefault(x => x.ElementId == parentClassElementId);
            return classSelection?.Level ?? 1;
        }

        private static AuroraExpressionEvaluationContext BuildInitialContext(
            AuroraCharacterStateDocument document,
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            IReadOnlyList<ActiveCharacterFeature> activeFeatures,
            SqliteConnection connection)
        {
            var context = new AuroraExpressionEvaluationContext();

            foreach (string token in document.Tokens ?? Enumerable.Empty<string>())
                context.AddToken(token);

            foreach (KeyValuePair<string, decimal> pair in document.NumericValues ?? new Dictionary<string, decimal>())
                context.AddNumericValue(pair.Key, pair.Value);

            foreach (KeyValuePair<string, string> pair in document.ScalarValues ?? new Dictionary<string, string>())
                context.AddScalarValue(pair.Key, pair.Value);

            foreach (KeyValuePair<string, List<string>> pair in document.MacroValues ?? new Dictionary<string, List<string>>())
                context.AddMacroValues(pair.Key, pair.Value);

            int totalLevel = directSelections
                .Where(x => string.Equals(x.TypeName, "Class", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Level.GetValueOrDefault(1));

            if (totalLevel > 0 && !context.NumericValues.ContainsKey("level"))
                context.AddNumericValue("level", totalLevel);

            foreach (ResolvedCharacterElement element in directSelections)
            {
                AddElementTokensToContext(context, connection, element.ElementId, element.AuroraId, element.Name);

                if (string.Equals(element.TypeName, "Class", StringComparison.OrdinalIgnoreCase))
                {
                    context.AddNumericValue($"{element.Name}:level", element.Level.GetValueOrDefault(1));
                    context.AddNumericValue($"{element.AuroraId}:level", element.Level.GetValueOrDefault(1));
                    context.AddMacroValues("$(class:list)", new[] { element.Name, element.AuroraId });
                }
            }

            foreach (ActiveCharacterFeature feature in activeFeatures)
                AddElementTokensToContext(context, connection, feature.ElementId, feature.AuroraId, feature.Name);

            return context;
        }

        private static void AddElementTokensToContext(
            AuroraExpressionEvaluationContext context,
            SqliteConnection connection,
            int elementId,
            string auroraId,
            string name)
        {
            if (!string.IsNullOrWhiteSpace(auroraId))
                context.AddToken(auroraId);

            if (!string.IsNullOrWhiteSpace(name))
                context.AddToken(name);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT support_text
FROM element_supports
WHERE element_id = $element_id
ORDER BY ordinal ASC;";
            command.Parameters.AddWithValue("$element_id", elementId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string supportText = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(supportText))
                    continue;

                context.AddToken(supportText);
                if (supportText.Contains("Spellcasting", StringComparison.OrdinalIgnoreCase)
                    || supportText.Contains("Spell", StringComparison.OrdinalIgnoreCase))
                {
                    context.AddMacroValues("$(spellcasting:list)", new[] { supportText });
                }
            }
        }

        private static Dictionary<int, int> BuildOwnerLevelMap(
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            IReadOnlyList<ActiveCharacterFeature> activeFeatures,
            SqliteConnection connection)
        {
            var levels = new Dictionary<int, int>();

            foreach (ResolvedCharacterElement selection in directSelections)
            {
                int level = selection.Level ?? 1;
                if (string.Equals(selection.TypeName, "Archetype", StringComparison.OrdinalIgnoreCase))
                    level = ResolveArchetypeLevel(selection, directSelections, connection);

                levels[selection.ElementId] = Math.Max(1, level);
            }

            foreach (ActiveCharacterFeature feature in activeFeatures)
                levels[feature.ElementId] = Math.Max(1, feature.UnlockLevel);

            return levels;
        }

        private static List<ActiveGrantResult> LoadActiveGrants(
            SqliteConnection connection,
            IReadOnlyDictionary<int, int> ownerLevels,
            AuroraExpressionEvaluationContext context)
        {
            if (ownerLevels.Count == 0)
                return new List<ActiveGrantResult>();

            string ownerIdList = string.Join(",", ownerLevels.Keys.OrderBy(x => x));
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    g.grant_id,
    owner.element_id,
    owner.name,
    owner_type.type_name,
    g.grant_type,
    g.grant_level,
    g.spellcasting_name,
    g.is_prepared,
    g.requirements_text,
    g.target_element_id,
    target.aurora_id,
    target.name,
    target_type.type_name,
    target_rec.package_key,
    g.target_semantic_key,
    g.target_semantic_kind,
    g.target_semantic_name
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN elements AS target
    ON target.element_id = g.target_element_id
LEFT JOIN element_types AS target_type
    ON target_type.element_type_id = target.element_type_id
LEFT JOIN resolved_elements_cache AS target_rec
    ON target_rec.winning_element_id = target.element_id
WHERE rs.owner_kind = 'element'
  AND rs.owner_element_id IN ({ownerIdList})
ORDER BY owner.name ASC, g.ordinal ASC;";

            var grants = new List<ActiveGrantResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int ownerElementId = reader.GetInt32(1);
                if (!ownerLevels.TryGetValue(ownerElementId, out int ownerLevel))
                    continue;

                int? grantLevel = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                if (grantLevel.HasValue && grantLevel.Value > ownerLevel)
                    continue;

                string requirementsText = reader.IsDBNull(6) ? null : reader.GetString(6);
                if (!IsRequirementSatisfied(requirementsText, context))
                    continue;

                grants.Add(new ActiveGrantResult(
                    reader.GetInt32(0),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    grantLevel,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7) != 0,
                    requirementsText,
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16)));
            }

            return grants;
        }

        private static void AddGrantTokensToContext(
            AuroraExpressionEvaluationContext context,
            IReadOnlyList<ActiveGrantResult> activeGrants,
            SqliteConnection connection)
        {
            foreach (ActiveGrantResult grant in activeGrants)
            {
                if (grant.TargetElementId.HasValue)
                {
                    AddElementTokensToContext(
                        context,
                        connection,
                        grant.TargetElementId.Value,
                        grant.TargetAuroraId,
                        grant.TargetName);
                }

                if (!string.IsNullOrWhiteSpace(grant.TargetSemanticKey))
                    context.AddToken(grant.TargetSemanticKey);
                if (!string.IsNullOrWhiteSpace(grant.TargetSemanticName))
                    context.AddToken(grant.TargetSemanticName);
            }
        }

        private static List<CharacterSelectResult> LoadAvailableSelects(
            SqliteConnection connection,
            IReadOnlyDictionary<int, int> ownerLevels,
            AuroraExpressionEvaluationContext context)
        {
            if (ownerLevels.Count == 0)
                return new List<CharacterSelectResult>();

            string ownerIdList = string.Join(",", ownerLevels.Keys.OrderBy(x => x));
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    s.select_id,
    owner.element_id,
    owner.name,
    owner_type.type_name,
    owner_rec.package_key,
    s.name_text,
    s.select_type,
    s.supports_text,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    s.requirements_text
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE rs.owner_kind = 'element'
  AND rs.owner_element_id IN ({ownerIdList})
ORDER BY owner.name ASC, s.ordinal ASC;";

            var selects = new List<CharacterSelectResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int ownerElementId = reader.GetInt32(1);
                if (!ownerLevels.TryGetValue(ownerElementId, out int ownerLevel))
                    continue;

                string supportsText = reader.IsDBNull(7) ? null : reader.GetString(7);
                int? selectLevel = reader.IsDBNull(8) ? null : reader.GetInt32(8);
                if (selectLevel.HasValue && selectLevel.Value > ownerLevel)
                    continue;

                string requirementsText = reader.IsDBNull(11) ? null : reader.GetString(11);
                if (!IsRequirementSatisfied(requirementsText, context))
                    continue;

                int selectId = reader.GetInt32(0);
                string selectType = reader.GetString(6);
                string selectPolicy = ClassifySelectPolicy(selectType, reader.IsDBNull(5) ? null : reader.GetString(5), supportsText);
                string choiceFamily = ClassifyChoiceFamily(selectType, selectPolicy, reader.IsDBNull(5) ? null : reader.GetString(5), supportsText);
                string ownerName = reader.GetString(2);
                string ownerTypeName = reader.GetString(3);
                string selectName = reader.IsDBNull(5) ? null : reader.GetString(5);
                List<CharacterSelectOptionResult> options = LoadSelectOptions(
                    connection,
                    selectId,
                    selectType,
                    selectPolicy,
                    supportsText,
                    context,
                    ownerName,
                    ownerTypeName,
                    selectName);
                selects.Add(new CharacterSelectResult(
                    selectId,
                    ownerName,
                    ownerTypeName,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    selectName,
                    selectType,
                    selectPolicy,
                    choiceFamily,
                    supportsText,
                    selectLevel,
                    reader.GetInt32(9),
                    !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
                    requirementsText,
                    options));
            }

            return selects;
        }

        private static List<CharacterSelectOptionResult> LoadSelectOptions(
            SqliteConnection connection,
            int selectId,
            string selectType,
            string selectPolicy,
            string supportsText,
            AuroraExpressionEvaluationContext context,
            string ownerName,
            string ownerTypeName,
            string selectName,
            bool includeElementOptionFollowUps = true)
        {
            selectType = selectType?.Trim();
            selectPolicy = selectPolicy?.Trim();

            if (string.Equals(selectPolicy, "broad-language-pool", StringComparison.OrdinalIgnoreCase))
                return LoadLanguageOptions(connection, supportsText, context, ownerTypeName, ownerName, selectName);

            if (string.Equals(selectPolicy, "broad-proficiency-pool", StringComparison.OrdinalIgnoreCase))
                return LoadProficiencyOptions(connection, supportsText, context, ownerTypeName, ownerName, selectName);

            if (string.Equals(selectPolicy, "broad-feat-pool", StringComparison.OrdinalIgnoreCase))
                return BuildFeatFollowUpOptions(connection, context);

            if (string.Equals(selectPolicy, "asi-feature-pool", StringComparison.OrdinalIgnoreCase))
                return LoadAsiFeatureOptions(connection, selectId, supportsText, context);

            return LoadGenericSelectableOptions(
                connection,
                selectId,
                selectType,
                context,
                ownerName,
                ownerTypeName,
                selectName,
                includeElementOptionFollowUps);
        }

        private static List<CharacterSelectOptionResult> LoadGenericSelectableOptions(
            SqliteConnection connection,
            int selectId,
            string selectType,
            AuroraExpressionEvaluationContext context,
            string ownerName,
            string ownerTypeName,
            string selectName,
            bool includeElementOptionFollowUps)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    option_kind,
    option_element_id,
    option_aurora_id,
    option_name,
    option_type_name,
    option_package_key,
    option_text
FROM v_selectable_options
WHERE select_id = $select_id
ORDER BY
    CASE option_kind
        WHEN 'element' THEN 0
        ELSE 1
    END,
    COALESCE(option_name, option_text, '') ASC;";
            command.Parameters.AddWithValue("$select_id", selectId);

            var options = new List<CharacterSelectOptionResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string optionKind = reader.GetString(0);
                int? optionElementId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                string optionAuroraId = reader.IsDBNull(2) ? null : reader.GetString(2);
                string optionName = reader.IsDBNull(3) ? null : reader.GetString(3);
                string optionTypeName = reader.IsDBNull(4) ? null : reader.GetString(4);
                string optionPackageKey = reader.IsDBNull(5) ? null : reader.GetString(5);
                string optionText = reader.IsDBNull(6) ? null : reader.GetString(6);

                string requirementText = null;
                bool isAvailable = true;
                bool isAlreadyOwned = false;
                bool isChosenForSelect = false;

                if (optionElementId.HasValue)
                {
                    if (!OptionMatchesSelectType(selectType, optionTypeName))
                        continue;

                    requirementText = LoadElementRequirementText(connection, optionElementId.Value);
                    isAvailable = IsRequirementSatisfied(requirementText, context);
                    isAlreadyOwned = (!string.IsNullOrWhiteSpace(optionAuroraId) && context.MatchesToken(optionAuroraId))
                                     || (!string.IsNullOrWhiteSpace(optionName) && context.MatchesToken(optionName));
                    isChosenForSelect = IsStoredChoiceValue(
                        context,
                        ownerTypeName,
                        ownerName,
                        selectName,
                        optionAuroraId,
                        optionName);
                }
                else
                {
                    isAlreadyOwned = IsStoredTextChoice(context, ownerTypeName, ownerName, selectName, optionText ?? optionName);
                    isChosenForSelect = isAlreadyOwned;
                }

                IReadOnlyList<CharacterSelectOptionResult> followUpOptions = null;
                string followUpKind = null;
                if (includeElementOptionFollowUps && optionElementId.HasValue && isAvailable)
                {
                    followUpOptions = LoadDirectSelectPreviewOptions(connection, optionElementId.Value, context);
                    if (followUpOptions.Count > 0)
                        followUpKind = "unlocked-selects";
                }

                options.Add(new CharacterSelectOptionResult(
                    optionKind,
                    optionElementId,
                    optionAuroraId,
                    optionName,
                    optionTypeName,
                    optionPackageKey,
                    optionText,
                    isAvailable,
                    isAlreadyOwned,
                    isChosenForSelect,
                    requirementText,
                    followUpKind,
                    followUpOptions));
            }

            return options
                .GroupBy(x => $"{x.OptionKind}|{x.OptionElementId?.ToString() ?? ""}|{x.OptionText ?? ""}")
                .Select(x => x.First())
                .ToList();
        }

        private static List<CharacterSelectOptionResult> LoadDirectSelectPreviewOptions(
            SqliteConnection connection,
            int ownerElementId,
            AuroraExpressionEvaluationContext baseContext)
        {
            AuroraExpressionEvaluationContext context = CloneContext(baseContext);
            string ownerAuroraId = null;
            string ownerName = null;
            string ownerTypeName = null;

            using (var ownerCommand = connection.CreateCommand())
            {
                ownerCommand.CommandText = @"
SELECT
    e.aurora_id,
    e.name,
    et.type_name
FROM elements AS e
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
WHERE element_id = $element_id;";
                ownerCommand.Parameters.AddWithValue("$element_id", ownerElementId);

                using var ownerReader = ownerCommand.ExecuteReader();
                if (ownerReader.Read())
                {
                    ownerAuroraId = ownerReader.IsDBNull(0) ? null : ownerReader.GetString(0);
                    ownerName = ownerReader.IsDBNull(1) ? null : ownerReader.GetString(1);
                    ownerTypeName = ownerReader.IsDBNull(2) ? null : ownerReader.GetString(2);
                }
            }

            AddElementTokensToContext(context, connection, ownerElementId, ownerAuroraId, ownerName);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    s.select_id,
    s.name_text,
    s.select_type,
    s.supports_text,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    s.requirements_text
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
WHERE rs.owner_kind = 'element'
  AND rs.owner_element_id = $owner_element_id
ORDER BY s.ordinal ASC;";
            command.Parameters.AddWithValue("$owner_element_id", ownerElementId);

            var previews = new List<CharacterSelectOptionResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int selectId = reader.GetInt32(0);
                string selectName = reader.IsDBNull(1) ? null : reader.GetString(1);
                string selectType = reader.GetString(2);
                string supportsText = reader.IsDBNull(3) ? null : reader.GetString(3);
                int? selectLevel = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                string requirementsText = reader.IsDBNull(7) ? null : reader.GetString(7);

                if (selectLevel.HasValue && selectLevel.Value > 1)
                    continue;

                bool isAvailable = IsRequirementSatisfied(requirementsText, context);
                if (!isAvailable)
                    continue;

                string selectPolicy = ClassifySelectPolicy(selectType, selectName, supportsText);
                List<CharacterSelectOptionResult> options = LoadSelectOptions(
                    connection,
                    selectId,
                    selectType,
                    selectPolicy,
                    supportsText,
                    context,
                    ownerName,
                    ownerTypeName,
                    selectName,
                    includeElementOptionFollowUps: false);

                int availableOptionCount = options.Count(x => x.IsAvailable);
                if (availableOptionCount == 0)
                    continue;

                previews.Add(new CharacterSelectOptionResult(
                    "select-preview",
                    null,
                    $"SELECT_PREVIEW_{selectId}",
                    $"{selectName ?? selectType} ({selectType})",
                    selectType,
                    null,
                    null,
                    true,
                    false,
                    false,
                    requirementsText,
                    $"select-preview:{selectPolicy}",
                    options));
            }

            return previews;
        }

        private static bool OptionMatchesSelectType(string selectType, string optionTypeName)
        {
            selectType = selectType?.Trim();
            optionTypeName = optionTypeName?.Trim();

            if (string.IsNullOrWhiteSpace(selectType) || string.IsNullOrWhiteSpace(optionTypeName))
                return true;

            if (string.Equals(selectType, optionTypeName, StringComparison.OrdinalIgnoreCase))
                return true;

            return selectType switch
            {
                "Class Feature" => optionTypeName is "Class Feature" or "Feat Feature" or "Ability Score Improvement",
                "Archetype Feature" => optionTypeName is "Archetype Feature" or "Class Feature",
                "Background Feature" => optionTypeName is "Background Feature" or "Background Variant",
                "Racial Trait" => optionTypeName is "Racial Trait" or "Race Variant" or "Dragonmark",
                _ => false
            };
        }

        private static List<CharacterSelectOptionResult> LoadLanguageOptions(
            SqliteConnection connection,
            string supportsText,
            AuroraExpressionEvaluationContext context,
            string ownerTypeName,
            string ownerName,
            string selectName)
        {
            List<string> supportAtoms = ExtractSupportAtoms(supportsText);
            bool allowStandard = supportAtoms.Any(x => string.Equals(x, "Standard", StringComparison.OrdinalIgnoreCase));
            bool allowExotic = supportAtoms.Any(x => string.Equals(x, "Exotic", StringComparison.OrdinalIgnoreCase));
            bool allowSecret = supportAtoms.Any(x => string.Equals(x, "Secret", StringComparison.OrdinalIgnoreCase));

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    rec.package_key,
    l.is_standard,
    l.is_exotic,
    l.is_secret
FROM languages AS l
JOIN elements AS e
    ON e.element_id = l.element_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = e.element_id
ORDER BY e.name ASC, rec.package_key ASC;";

            var options = new List<CharacterSelectOptionResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                bool isStandard = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
                bool isExotic = !reader.IsDBNull(5) && reader.GetInt32(5) != 0;
                bool isSecret = !reader.IsDBNull(6) && reader.GetInt32(6) != 0;

                bool include = true;
                if (allowStandard || allowExotic || allowSecret)
                {
                    include = (allowStandard && isStandard)
                              || (allowExotic && isExotic)
                              || (allowSecret && isSecret);
                }

                if (!include)
                    continue;

                string optionAuroraId = reader.GetString(1);
                string optionName = reader.GetString(2);
                bool isAlreadyOwned = context.MatchesToken(optionAuroraId) || context.MatchesToken(optionName);

                options.Add(new CharacterSelectOptionResult(
                    "element",
                    reader.GetInt32(0),
                    optionAuroraId,
                    optionName,
                    "Language",
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    null,
                    true,
                    isAlreadyOwned,
                    IsStoredChoiceValue(context, ownerTypeName, ownerName, selectName, optionAuroraId, optionName),
                    LoadElementRequirementText(connection, reader.GetInt32(0))));
            }

            return options
                .GroupBy(x => x.OptionElementId)
                .Select(x => x.First())
                .ToList();
        }

        private static List<CharacterSelectOptionResult> LoadAsiFeatureOptions(
            SqliteConnection connection,
            int selectId,
            string supportsText,
            AuroraExpressionEvaluationContext context)
        {
            bool featAllowed = context.MatchesToken("ID_INTERNAL_OPTION_ALLOW_FEATS");
            List<CharacterSelectOptionResult> asiFollowUps = BuildAsiFollowUpOptions(context);
            List<CharacterSelectOptionResult> featFollowUps = featAllowed
                ? BuildFeatFollowUpOptions(connection, context)
                : new List<CharacterSelectOptionResult>();

            return new List<CharacterSelectOptionResult>
            {
                new(
                    "semantic",
                    null,
                    "SEMANTIC_ASI",
                    "Ability Score Improvement",
                    "Ability Score Improvement",
                    null,
                    null,
                    true,
                    false,
                    false,
                    null,
                    "ability-score-improvement",
                    asiFollowUps),
                new(
                    "semantic",
                    null,
                    "SEMANTIC_FEAT",
                    "Feat",
                    "Feat",
                    null,
                    null,
                    featAllowed,
                    false,
                    false,
                    "ID_INTERNAL_OPTION_ALLOW_FEATS",
                    "feat-selection",
                    featFollowUps)
            };
        }

        private static List<CharacterSelectOptionResult> BuildAsiFollowUpOptions(AuroraExpressionEvaluationContext context)
        {
            var options = new List<CharacterSelectOptionResult>();
            string[] abilityKeys = { "str", "dex", "con", "int", "wis", "cha" };

            foreach (string abilityKey in abilityKeys)
            {
                decimal currentValue = GetAbilityScore(context, abilityKey);
                bool isAvailable = currentValue <= 18m;
                string abilityName = GetAbilityDisplayName(abilityKey);

                options.Add(new CharacterSelectOptionResult(
                    "semantic",
                    null,
                    $"ASI_PLUS2_{abilityKey.ToUpperInvariant()}",
                    $"+2 {abilityName}",
                    "Ability Score Improvement",
                    null,
                    BuildAsiPayload("plus2", abilityKey),
                    isAvailable,
                    false,
                    false,
                    isAvailable ? null : "Ability score would exceed 20."));
            }

            for (int leftIndex = 0; leftIndex < abilityKeys.Length; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1; rightIndex < abilityKeys.Length; rightIndex++)
                {
                    string leftAbility = abilityKeys[leftIndex];
                    string rightAbility = abilityKeys[rightIndex];
                    decimal leftValue = GetAbilityScore(context, leftAbility);
                    decimal rightValue = GetAbilityScore(context, rightAbility);
                    bool isAvailable = leftValue <= 19m && rightValue <= 19m;

                    options.Add(new CharacterSelectOptionResult(
                        "semantic",
                        null,
                        $"ASI_PLUS1_{leftAbility.ToUpperInvariant()}_{rightAbility.ToUpperInvariant()}",
                        $"+1 {GetAbilityDisplayName(leftAbility)} / +1 {GetAbilityDisplayName(rightAbility)}",
                        "Ability Score Improvement",
                        null,
                        BuildAsiPayload("plus1plus1", leftAbility, rightAbility),
                        isAvailable,
                        false,
                        false,
                        isAvailable ? null : "One or more ability scores would exceed 20."));
                }
            }

            return options;
        }

        private static List<CharacterSelectOptionResult> BuildFeatFollowUpOptions(
            SqliteConnection connection,
            AuroraExpressionEvaluationContext context)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    rec.package_key
FROM elements AS e
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = e.element_id
WHERE et.type_name = 'Feat'
ORDER BY e.name ASC, rec.package_key ASC;";

            var options = new List<CharacterSelectOptionResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int elementId = reader.GetInt32(0);
                string auroraId = reader.GetString(1);
                string featName = reader.GetString(2);
                string packageKey = reader.IsDBNull(3) ? null : reader.GetString(3);
                string requirementText = LoadElementRequirementText(connection, elementId);
                bool isAvailable = IsRequirementSatisfied(requirementText, context);
                bool isAlreadyOwned = context.MatchesToken(auroraId) || context.MatchesToken(featName);

                options.Add(new CharacterSelectOptionResult(
                    "element",
                    elementId,
                    auroraId,
                    featName,
                    "Feat",
                    packageKey,
                    null,
                    isAvailable,
                    isAlreadyOwned,
                    false,
                    requirementText));
            }

            return options
                .GroupBy(x => x.OptionElementId)
                .Select(x => x.First())
                .OrderBy(x => x.OptionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.OptionPackageKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<CharacterSelectOptionResult> LoadProficiencyOptions(
            SqliteConnection connection,
            string supportsText,
            AuroraExpressionEvaluationContext context,
            string ownerTypeName,
            string ownerName,
            string selectName)
        {
            List<string> supportAtoms = ExtractSupportAtoms(supportsText);
            string groupFilter = ResolveProficiencyGroupFilter(supportAtoms);
            List<string> specificFilters = ResolveSpecificSupportFilters(supportAtoms, groupFilter);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    rec.package_key,
    p.proficiency_group,
    p.proficiency_subgroup,
    GROUP_CONCAT(es.support_text, '|') AS support_blob
FROM proficiencies AS p
JOIN elements AS e
    ON e.element_id = p.element_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = e.element_id
LEFT JOIN element_supports AS es
    ON es.element_id = e.element_id
GROUP BY
    e.element_id,
    e.aurora_id,
    e.name,
    rec.package_key,
    p.proficiency_group,
    p.proficiency_subgroup
ORDER BY e.name ASC, rec.package_key ASC;";

            var options = new List<CharacterSelectOptionResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string proficiencyName = reader.GetString(2);
                string proficiencyGroup = reader.IsDBNull(4) ? null : reader.GetString(4);
                string proficiencySubgroup = reader.IsDBNull(5) ? null : reader.GetString(5);
                string supportBlob = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);

                if (!ProficiencyMatchesGroup(groupFilter, proficiencyGroup, proficiencySubgroup, supportBlob, proficiencyName))
                    continue;

                if (specificFilters.Count > 0)
                {
                    bool specificMatch = specificFilters.Any(filter =>
                        supportBlob.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!specificMatch)
                        continue;
                }

                string optionAuroraId = reader.GetString(1);
                bool isAlreadyOwned = context.MatchesToken(optionAuroraId) || context.MatchesToken(proficiencyName);
                options.Add(new CharacterSelectOptionResult(
                    "element",
                    reader.GetInt32(0),
                    optionAuroraId,
                    proficiencyName,
                    "Proficiency",
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    null,
                    true,
                    isAlreadyOwned,
                    IsStoredChoiceValue(context, ownerTypeName, ownerName, selectName, optionAuroraId, proficiencyName),
                    LoadElementRequirementText(connection, reader.GetInt32(0))));
            }

            return options
                .GroupBy(x => x.OptionElementId)
                .Select(x => x.First())
                .ToList();
        }

        private static List<string> ExtractSupportAtoms(string supportsText)
        {
            if (string.IsNullOrWhiteSpace(supportsText))
                return new List<string>();

            char[] separators = { ',', '|', '&', '(', ')', '!' };
            return supportsText
                .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ResolveProficiencyGroupFilter(IReadOnlyList<string> supportAtoms)
        {
            if (supportAtoms.Any(x => string.Equals(x, "Skill", StringComparison.OrdinalIgnoreCase)))
                return "Skill";
            if (supportAtoms.Any(x => string.Equals(x, "Tool", StringComparison.OrdinalIgnoreCase)
                                      || x.Contains("Tool", StringComparison.OrdinalIgnoreCase)))
                return "Tool";
            if (supportAtoms.Any(x => string.Equals(x, "Armor", StringComparison.OrdinalIgnoreCase)
                                      || x.Contains("Armor", StringComparison.OrdinalIgnoreCase)))
                return "Armor";
            if (supportAtoms.Any(x => string.Equals(x, "Weapon", StringComparison.OrdinalIgnoreCase)
                                      || x.Contains("Weapon", StringComparison.OrdinalIgnoreCase)))
                return "Weapon";
            if (supportAtoms.Any(x => string.Equals(x, "Saving Throw", StringComparison.OrdinalIgnoreCase)))
                return "Saving Throw";

            return null;
        }

        private static string BuildChoiceMacroName(string ownerTypeName, string ownerName, string selectName)
        {
            return $"$(choice||{ownerTypeName?.Trim() ?? string.Empty}||{ownerName?.Trim() ?? string.Empty}||{selectName?.Trim() ?? string.Empty})";
        }

        private static bool TryParseChoiceMacroName(
            string macroName,
            out string ownerTypeName,
            out string ownerName,
            out string selectName)
        {
            ownerTypeName = null;
            ownerName = null;
            selectName = null;

            if (string.IsNullOrWhiteSpace(macroName))
                return false;

            string text = macroName.Trim();
            if (!text.StartsWith("$(choice||", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(")", StringComparison.Ordinal))
                return false;

            string inner = text.Substring("$(choice||".Length, text.Length - "$(choice||".Length - 1);
            string[] parts = inner.Split("||", StringSplitOptions.None);
            if (parts.Length != 3)
                return false;

            ownerTypeName = parts[0];
            ownerName = parts[1];
            selectName = parts[2];
            return true;
        }

        private static bool IsStoredTextChoice(
            AuroraExpressionEvaluationContext context,
            string ownerTypeName,
            string ownerName,
            string selectName,
            string value)
        {
            if (context == null || string.IsNullOrWhiteSpace(value))
                return false;

            string macroName = BuildChoiceMacroName(ownerTypeName, ownerName, selectName);
            return context.MacroValues.TryGetValue(macroName, out HashSet<string> values)
                   && values.Contains(value.Trim());
        }

        private static bool IsStoredChoiceValue(
            AuroraExpressionEvaluationContext context,
            string ownerTypeName,
            string ownerName,
            string selectName,
            params string[] values)
        {
            if (context == null || values == null || values.Length == 0)
                return false;

            string macroName = BuildChoiceMacroName(ownerTypeName, ownerName, selectName);
            if (!context.MacroValues.TryGetValue(macroName, out HashSet<string> storedValues) || storedValues == null)
                return false;

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && storedValues.Contains(value.Trim()))
                    return true;
            }

            return false;
        }

        private static bool StoreChoiceValue(
            AuroraCharacterStateDocument document,
            CharacterSelectResult select,
            params string[] values)
        {
            if (document == null || select == null || values == null || values.Length == 0)
                return false;

            string macroName = BuildChoiceMacroName(select.OwnerTypeName, select.OwnerName, select.SelectName);
            if (!document.MacroValues.TryGetValue(macroName, out List<string> storedValues) || storedValues == null)
            {
                storedValues = new List<string>();
                document.MacroValues[macroName] = storedValues;
            }

            bool changed = false;
            foreach (string value in values)
            {
                string trimmed = value?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (storedValues.Any(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)))
                    continue;

                storedValues.Add(trimmed);
                changed = true;
            }

            return changed;
        }

        private static List<string> ResolveSpecificSupportFilters(IReadOnlyList<string> supportAtoms, string groupFilter)
        {
            return supportAtoms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !string.Equals(x, groupFilter, StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.Equals(x, "Standard", StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.Equals(x, "Exotic", StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.Equals(x, "Secret", StringComparison.OrdinalIgnoreCase))
                .Where(x => !int.TryParse(x, out _))
                .ToList();
        }

        private static bool ProficiencyMatchesGroup(
            string groupFilter,
            string proficiencyGroup,
            string proficiencySubgroup,
            string supportBlob,
            string proficiencyName)
        {
            if (string.IsNullOrWhiteSpace(groupFilter))
                return true;

            return groupFilter switch
            {
                "Skill" => string.Equals(proficiencyGroup, "Skill", StringComparison.OrdinalIgnoreCase),
                "Tool" => (proficiencyGroup?.Contains("Tool", StringComparison.OrdinalIgnoreCase) ?? false)
                          || (proficiencySubgroup?.Contains("Tool", StringComparison.OrdinalIgnoreCase) ?? false)
                          || (supportBlob?.Contains("Tool", StringComparison.OrdinalIgnoreCase) ?? false)
                          || proficiencyName.Contains("Tool", StringComparison.OrdinalIgnoreCase),
                "Armor" => (proficiencyGroup?.Contains("Armor", StringComparison.OrdinalIgnoreCase) ?? false)
                           || (supportBlob?.Contains("Armor", StringComparison.OrdinalIgnoreCase) ?? false),
                "Weapon" => (proficiencyGroup?.Contains("Weapon", StringComparison.OrdinalIgnoreCase) ?? false)
                            || (supportBlob?.Contains("Weapon", StringComparison.OrdinalIgnoreCase) ?? false),
                "Saving Throw" => string.Equals(proficiencyGroup, "Saving Throw", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private static ComputedCharacterResult BuildComputedCharacter(
            SqliteConnection connection,
            AuroraCharacterStateDocument originalDocument,
            AuroraCharacterStateDocument workingDocument,
            CharacterEvaluationResult evaluation)
        {
            var provenance = new List<CharacterProvenanceEntry>();
            List<ComputedAbilityScoreResult> abilityScores = BuildComputedAbilityScores(originalDocument, evaluation, provenance);
            List<ComputedCharacterItemResult> proficiencies = BuildComputedProficiencies(evaluation, provenance);
            List<ComputedCharacterItemResult> languages = BuildComputedLanguages(evaluation, provenance);
            List<ComputedCharacterItemResult> feats = BuildComputedFeats(evaluation, provenance);
            List<ComputedCharacterItemResult> features = BuildComputedFeatures(evaluation, provenance);
            List<ComputedGrantedSpellResult> grantedSpells = BuildComputedGrantedSpells(evaluation, provenance);
            List<ComputedSpellcastingProfileResult> spellcastingProfiles = BuildComputedSpellcastingProfiles(grantedSpells);
            List<ComputedCharacterItemResult> choiceSelections = BuildComputedChoiceSelections(workingDocument, provenance);
            List<ComputedCharacterItemResult> traits = BuildComputedTraits(connection, evaluation, provenance);
            List<ComputedEffectRowResult> effectRows = BuildComputedEffectRows(
                abilityScores,
                proficiencies,
                languages,
                feats,
                features,
                grantedSpells,
                traits);
            List<PendingCharacterChoiceResult> pendingChoices = BuildPendingChoices(
                connection,
                evaluation.AvailableSelects,
                evaluation.AppliedChoices,
                evaluation.DirectSelections,
                workingDocument);
            List<CharacterWarningResult> warnings = BuildCharacterWarnings(
                connection,
                evaluation.AppliedChoices,
                pendingChoices,
                evaluation.AvailableSelects,
                evaluation.DirectSelections,
                workingDocument);

            return new ComputedCharacterResult(
                abilityScores,
                proficiencies,
                languages,
                feats,
                features,
                grantedSpells,
                spellcastingProfiles,
                effectRows,
                choiceSelections,
                traits,
                pendingChoices,
                warnings,
                provenance
                    .Distinct()
                    .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ElementName, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        private static List<ComputedAbilityScoreResult> BuildComputedAbilityScores(
            AuroraCharacterStateDocument originalDocument,
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            string[] abilityKeys = { "str", "dex", "con", "int", "wis", "cha" };
            var results = new List<ComputedAbilityScoreResult>();

            foreach (string abilityKey in abilityKeys)
            {
                decimal baseValue = originalDocument.NumericValues.TryGetValue(abilityKey, out decimal originalValue)
                    ? originalValue
                    : 0m;
                decimal finalValue = evaluation.EvaluationContext.NumericValues.TryGetValue(abilityKey, out decimal currentValue)
                    ? currentValue
                    : baseValue;

                if (baseValue == 0m
                    && finalValue == 0m
                    && !originalDocument.NumericValues.ContainsKey(abilityKey)
                    && !evaluation.EvaluationContext.NumericValues.ContainsKey(abilityKey))
                {
                    continue;
                }

                string abilityName = GetAbilityDisplayName(abilityKey);
                string key = $"ability:{abilityKey}";
                List<CharacterProvenanceEntry> itemProvenance = BuildAbilityProvenance(
                    abilityKey,
                    abilityName,
                    baseValue,
                    finalValue,
                    evaluation.AppliedChoices);
                provenanceSink.AddRange(itemProvenance);

                results.Add(new ComputedAbilityScoreResult(
                    abilityKey,
                    abilityName,
                    baseValue,
                    finalValue - baseValue,
                    finalValue,
                    itemProvenance));
            }

            return results;
        }

        private static List<CharacterProvenanceEntry> BuildAbilityProvenance(
            string abilityKey,
            string abilityName,
            decimal baseValue,
            decimal finalValue,
            IReadOnlyList<AppliedCharacterChoiceResult> appliedChoices)
        {
            string key = $"ability:{abilityKey}";
            var provenance = new List<CharacterProvenanceEntry>
            {
                new(
                    "ability-score",
                    key,
                    "base-state",
                    null,
                    null,
                    null,
                    null,
                    abilityName,
                    $"Base value {baseValue}")
            };

            foreach (AppliedCharacterChoiceResult choice in appliedChoices
                         .Where(x => string.Equals(x.Status, "applied", StringComparison.OrdinalIgnoreCase))
                         .Where(x => !string.IsNullOrWhiteSpace(x.FollowUpOptionAuroraId))
                         .Where(x => x.FollowUpOptionAuroraId.IndexOf(abilityKey, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                provenance.Add(new CharacterProvenanceEntry(
                    "ability-score",
                    key,
                    "semantic-choice",
                    choice.OwnerName,
                    choice.OwnerTypeName,
                    null,
                    choice.FollowUpOptionAuroraId,
                    choice.FollowUpOptionName ?? abilityName,
                    choice.SelectName));
            }

            if (provenance.Count == 1 && finalValue != baseValue)
            {
                provenance.Add(new CharacterProvenanceEntry(
                    "ability-score",
                    key,
                    "derived-state",
                    null,
                    null,
                    null,
                    null,
                    abilityName,
                    $"Final value {finalValue} differs from base but no explicit semantic choice was recorded."));
            }

            return provenance;
        }

        private static List<ComputedCharacterItemResult> BuildComputedProficiencies(
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (ResolvedCharacterElement selection in evaluation.DirectSelections
                         .Where(x => string.Equals(x.TypeName, "Proficiency", StringComparison.OrdinalIgnoreCase)))
            {
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "proficiency",
                        selection.AuroraId ?? selection.Name,
                        "direct-selection",
                        selection.Name,
                        selection.TypeName,
                        selection.PackageKey,
                        selection.AuroraId,
                        selection.Name,
                        selection.SourcePath)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "proficiency",
                    selection.AuroraId ?? selection.Name,
                    selection.Name,
                    selection.TypeName,
                    selection.PackageKey,
                    true,
                    provenance));
            }

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(x => string.Equals(x.TargetTypeName, "Proficiency", StringComparison.OrdinalIgnoreCase)))
            {
                string key = grant.TargetAuroraId ?? grant.TargetName ?? $"grant:{grant.GrantId}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "proficiency",
                        key,
                        "grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        grant.TargetName,
                        grant.RequirementsText)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "proficiency",
                    key,
                    grant.TargetName ?? key,
                    grant.TargetTypeName ?? "Proficiency",
                    grant.TargetPackageKey,
                    false,
                    provenance));
            }

            return MergeComputedItems(items);
        }

        private static List<ComputedCharacterItemResult> BuildComputedLanguages(
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (ResolvedCharacterElement selection in evaluation.DirectSelections
                         .Where(x => string.Equals(x.TypeName, "Language", StringComparison.OrdinalIgnoreCase)))
            {
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "language",
                        selection.AuroraId ?? selection.Name,
                        "direct-selection",
                        selection.Name,
                        selection.TypeName,
                        selection.PackageKey,
                        selection.AuroraId,
                        selection.Name,
                        selection.SourcePath)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "language",
                    selection.AuroraId ?? selection.Name,
                    selection.Name,
                    selection.TypeName,
                    selection.PackageKey,
                    true,
                    provenance));
            }

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(x => string.Equals(x.TargetTypeName, "Language", StringComparison.OrdinalIgnoreCase)))
            {
                string key = grant.TargetAuroraId ?? grant.TargetName ?? $"grant:{grant.GrantId}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "language",
                        key,
                        "grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        grant.TargetName,
                        grant.RequirementsText)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "language",
                    key,
                    grant.TargetName ?? key,
                    grant.TargetTypeName ?? "Language",
                    grant.TargetPackageKey,
                    false,
                    provenance));
            }

            return MergeComputedItems(items);
        }

        private static List<ComputedCharacterItemResult> BuildComputedFeats(
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (ResolvedCharacterElement selection in evaluation.DirectSelections
                         .Where(x => string.Equals(x.TypeName, "Feat", StringComparison.OrdinalIgnoreCase)))
            {
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "feat",
                        selection.AuroraId ?? selection.Name,
                        "direct-selection",
                        selection.Name,
                        selection.TypeName,
                        selection.PackageKey,
                        selection.AuroraId,
                        selection.Name,
                        selection.SourcePath)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "feat",
                    selection.AuroraId ?? selection.Name,
                    selection.Name,
                    selection.TypeName,
                    selection.PackageKey,
                    true,
                    provenance));
            }

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(x => string.Equals(x.TargetTypeName, "Feat", StringComparison.OrdinalIgnoreCase)))
            {
                string key = grant.TargetAuroraId ?? grant.TargetName ?? $"grant:{grant.GrantId}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "feat",
                        key,
                        "grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        grant.TargetName,
                        grant.RequirementsText)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "feat",
                    key,
                    grant.TargetName ?? key,
                    grant.TargetTypeName ?? "Feat",
                    grant.TargetPackageKey,
                    false,
                    provenance));
            }

            return MergeComputedItems(items);
        }

        private static List<ComputedCharacterItemResult> BuildComputedFeatures(
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (ResolvedCharacterElement selection in evaluation.DirectSelections.Where(x => IsFeatureLikeSelectionType(x.TypeName)))
            {
                string key = selection.AuroraId ?? $"{selection.TypeName}:{selection.Name}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "feature",
                        key,
                        "direct-selection",
                        selection.Name,
                        selection.TypeName,
                        selection.PackageKey,
                        selection.AuroraId,
                        selection.Name,
                        "Selected directly")
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "feature",
                    key,
                    selection.Name,
                    selection.TypeName,
                    selection.PackageKey,
                    true,
                    provenance));
            }

            foreach (ActiveCharacterFeature feature in evaluation.ActiveFeatures)
            {
                string key = feature.AuroraId ?? $"{feature.OwnerTypeName}:{feature.OwnerName}:{feature.Name}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "feature",
                        key,
                        "active-feature",
                        feature.OwnerName,
                        feature.OwnerTypeName,
                        feature.PackageKey,
                        feature.AuroraId,
                        feature.Name,
                        $"Unlock level {feature.UnlockLevel}")
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "feature",
                    key,
                    feature.Name,
                    feature.TypeName,
                    feature.PackageKey,
                    false,
                    provenance));
            }

            return MergeComputedItems(items);
        }

        private static bool IsFeatureLikeSelectionType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            return typeName.Contains("Feature", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(typeName, "Racial Trait", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(typeName, "Dragonmark", StringComparison.OrdinalIgnoreCase);
        }

        private static List<ComputedGrantedSpellResult> BuildComputedGrantedSpells(
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var spells = new List<ComputedGrantedSpellResult>();

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(x => string.Equals(x.TargetTypeName, "Spell", StringComparison.OrdinalIgnoreCase)))
            {
                string key = grant.TargetAuroraId ?? grant.TargetName ?? $"grant:{grant.GrantId}";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "granted-spell",
                        key,
                        "grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        grant.TargetName,
                        grant.SpellcastingName ?? grant.RequirementsText)
                };

                provenanceSink.AddRange(provenance);
                spells.Add(new ComputedGrantedSpellResult(
                    key,
                    grant.TargetAuroraId,
                    grant.TargetName ?? key,
                    grant.TargetPackageKey,
                    grant.SpellcastingName,
                    grant.IsPrepared,
                    grant.GrantLevel,
                    provenance));
            }

            return spells
                .GroupBy(
                    x => string.Join(
                        "|",
                        x.SpellAuroraId ?? string.Empty,
                        x.SpellName ?? string.Empty,
                        x.SpellPackageKey ?? string.Empty,
                        x.SpellcastingName ?? string.Empty,
                        x.IsPrepared?.ToString() ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    ComputedGrantedSpellResult first = group.First();
                    return new ComputedGrantedSpellResult(
                        first.SpellKey,
                        first.SpellAuroraId,
                        first.SpellName,
                        first.SpellPackageKey,
                        first.SpellcastingName,
                        first.IsPrepared,
                        first.GrantLevel,
                        group.SelectMany(x => x.Provenance)
                            .Distinct()
                            .OrderBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                            .ToList());
                })
                .OrderBy(x => x.SpellcastingName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SpellName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ComputedCharacterItemResult> BuildComputedChoiceSelections(
            AuroraCharacterStateDocument workingDocument,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (KeyValuePair<string, List<string>> pair in workingDocument.MacroValues ?? new Dictionary<string, List<string>>())
            {
                if (!TryParseChoiceMacroName(pair.Key, out string ownerTypeName, out string ownerName, out string selectName))
                    continue;

                foreach (string value in pair.Value ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    string key = $"{pair.Key}|{value.Trim()}";
                    List<CharacterProvenanceEntry> provenance = new()
                    {
                        new(
                            "choice-selection",
                            key,
                            "selected-choice",
                            ownerName,
                            ownerTypeName,
                            null,
                            null,
                            value.Trim(),
                            selectName)
                    };
                    provenanceSink.AddRange(provenance);
                    items.Add(new ComputedCharacterItemResult(
                        "choice-selection",
                        key,
                        value.Trim(),
                        selectName,
                        null,
                        false,
                        provenance));
                }
            }

            return MergeComputedItems(items);
        }

        private static List<ComputedSpellcastingProfileResult> BuildComputedSpellcastingProfiles(
            IReadOnlyList<ComputedGrantedSpellResult> grantedSpells)
        {
            return (grantedSpells ?? Array.Empty<ComputedGrantedSpellResult>())
                .GroupBy(
                    spell => string.IsNullOrWhiteSpace(spell.SpellcastingName) ? "(unspecified)" : spell.SpellcastingName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    string spellcastingName = group.Key;
                    string profileKey = $"spellcasting:{NormalizeContractToken(spellcastingName)}";
                    List<CharacterProvenanceEntry> provenance = group
                        .SelectMany(x => x.Provenance ?? Array.Empty<CharacterProvenanceEntry>())
                        .Distinct()
                        .OrderBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.ElementName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ComputedSpellcastingProfileResult(
                        profileKey,
                        spellcastingName,
                        group.Count(),
                        group.Count(x => x.IsPrepared == true),
                        group.Count(x => x.IsPrepared == false),
                        group.Select(x => x.SpellKey)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        provenance);
                })
                .OrderBy(x => x.SpellcastingName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ComputedEffectRowResult> BuildComputedEffectRows(
            IReadOnlyList<ComputedAbilityScoreResult> abilityScores,
            IReadOnlyList<ComputedCharacterItemResult> proficiencies,
            IReadOnlyList<ComputedCharacterItemResult> languages,
            IReadOnlyList<ComputedCharacterItemResult> feats,
            IReadOnlyList<ComputedCharacterItemResult> features,
            IReadOnlyList<ComputedGrantedSpellResult> grantedSpells,
            IReadOnlyList<ComputedCharacterItemResult> traits)
        {
            var rows = new List<ComputedEffectRowResult>();

            foreach (ComputedAbilityScoreResult ability in abilityScores ?? Array.Empty<ComputedAbilityScoreResult>())
            {
                rows.Add(new ComputedEffectRowResult(
                    "ability-score",
                    ability.AbilityKey,
                    $"ability:{ability.AbilityKey}",
                    ability.AbilityName,
                    ability.FinalValue.ToString(CultureInfo.InvariantCulture),
                    ability.FinalValue,
                    null,
                    false,
                    ability.Provenance));
            }

            AddItemEffectRows(rows, proficiencies, "proficiency", item => InferProficiencyEffectSubkind(item.Name, item.TypeName));
            AddItemEffectRows(rows, languages, "language", _ => "language");
            AddItemEffectRows(rows, feats, "feat", _ => "feat");
            AddItemEffectRows(rows, features, "feature", item => NormalizeContractToken(item.TypeName ?? item.Category));

            foreach (ComputedGrantedSpellResult spell in grantedSpells ?? Array.Empty<ComputedGrantedSpellResult>())
            {
                rows.Add(new ComputedEffectRowResult(
                    "spell-grant",
                    spell.IsPrepared == true ? "prepared-spell" : "spell",
                    $"spell:{spell.SpellKey}|{spell.SpellcastingName ?? string.Empty}|prepared={spell.IsPrepared?.ToString() ?? string.Empty}",
                    spell.SpellName,
                    spell.SpellcastingName,
                    spell.GrantLevel,
                    spell.SpellPackageKey,
                    false,
                    spell.Provenance));
            }

            foreach (ComputedCharacterItemResult trait in traits ?? Array.Empty<ComputedCharacterItemResult>())
            {
                string effectKind;
                string effectSubkind;
                string valueText = null;
                decimal? numericValue = null;

                if (string.Equals(trait.Category, "movement", StringComparison.OrdinalIgnoreCase)
                    || IsMovementKind(trait.TypeName))
                {
                    effectKind = "movement";
                    effectSubkind = NormalizeMovementKind(trait.TypeName) ?? "walk";
                    valueText = TryExtractMovementValueText(trait);
                    numericValue = TryParseDecimalInvariant(valueText);
                }
                else if (string.Equals(trait.Category, "sense", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(trait.TypeName, "sense", StringComparison.OrdinalIgnoreCase))
                {
                    effectKind = "sense";
                    effectSubkind = NormalizeContractToken(trait.Name);
                    valueText = trait.Name;
                }
                else if (string.Equals(trait.TypeName, "Size", StringComparison.OrdinalIgnoreCase)
                         || trait.Key.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
                {
                    effectKind = "size";
                    effectSubkind = "size";
                    valueText = trait.Name;
                }
                else
                {
                    effectKind = "trait";
                    effectSubkind = NormalizeContractToken(trait.TypeName ?? trait.Category);
                    valueText = trait.Name;
                }

                rows.Add(new ComputedEffectRowResult(
                    effectKind,
                    effectSubkind,
                    trait.Key,
                    trait.Name,
                    valueText,
                    numericValue,
                    trait.PackageKey,
                    trait.IsDirectSelection,
                    trait.Provenance));
            }

            return MergeComputedEffectRows(rows);
        }

        private static void AddItemEffectRows(
            ICollection<ComputedEffectRowResult> rows,
            IReadOnlyList<ComputedCharacterItemResult> items,
            string effectKind,
            Func<ComputedCharacterItemResult, string> effectSubkindSelector)
        {
            foreach (ComputedCharacterItemResult item in items ?? Array.Empty<ComputedCharacterItemResult>())
            {
                rows.Add(new ComputedEffectRowResult(
                    effectKind,
                    effectSubkindSelector(item),
                    item.Key,
                    item.Name,
                    item.Name,
                    null,
                    item.PackageKey,
                    item.IsDirectSelection,
                    item.Provenance));
            }
        }

        private static string InferProficiencyEffectSubkind(string itemName, string typeName)
        {
            string source = $"{itemName} {typeName}".Trim();
            if (source.IndexOf("skill", StringComparison.OrdinalIgnoreCase) >= 0)
                return "skill";
            if (source.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
                return "tool";
            if (source.IndexOf("armor", StringComparison.OrdinalIgnoreCase) >= 0)
                return "armor";
            if (source.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                return "weapon";
            if (source.IndexOf("saving throw", StringComparison.OrdinalIgnoreCase) >= 0)
                return "saving-throw";
            return "proficiency";
        }

        private static string TryExtractMovementValueText(ComputedCharacterItemResult movement)
        {
            if (movement == null)
                return null;

            string[] keyParts = (movement.Key ?? string.Empty).Split(':');
            if (keyParts.Length >= 3)
                return keyParts[^1];

            string name = movement.Name ?? string.Empty;
            int colonIndex = name.LastIndexOf(':');
            return colonIndex >= 0 && colonIndex < name.Length - 1
                ? name[(colonIndex + 1)..].Trim()
                : name.Trim();
        }

        private static decimal? TryParseDecimalInvariant(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            if (decimal.TryParse(rawValue.Trim().Replace("ft.", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
                return value;

            return null;
        }

        private static string NormalizeContractToken(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return "unspecified";

            string normalized = rawText.Trim().ToLowerInvariant();
            normalized = normalized.Replace("/", "-");
            normalized = normalized.Replace("_", "-");
            normalized = normalized.Replace(" ", "-");

            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

            return normalized.Trim('-');
        }

        private static List<ComputedEffectRowResult> MergeComputedEffectRows(IEnumerable<ComputedEffectRowResult> rows)
        {
            return (rows ?? Enumerable.Empty<ComputedEffectRowResult>())
                .GroupBy(row => new
                {
                    Kind = row.EffectKind ?? string.Empty,
                    Subkind = row.EffectSubkind ?? string.Empty,
                    Key = row.EffectKey ?? string.Empty,
                    Value = row.ValueText ?? string.Empty
                })
                .Select(group =>
                {
                    ComputedEffectRowResult first = group.First();
                    List<CharacterProvenanceEntry> provenance = group
                        .SelectMany(x => x.Provenance ?? Array.Empty<CharacterProvenanceEntry>())
                        .Distinct()
                        .OrderBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.ElementName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ComputedEffectRowResult(
                        first.EffectKind,
                        first.EffectSubkind,
                        first.EffectKey,
                        group.Select(x => x.DisplayName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? first.DisplayName,
                        group.Select(x => x.ValueText).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? first.ValueText,
                        group.Select(x => x.NumericValue).FirstOrDefault(x => x.HasValue),
                        group.Select(x => x.PackageKey).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? first.PackageKey,
                        group.Any(x => x.IsDirectSelection),
                        provenance);
                })
                .OrderBy(x => x.EffectKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.EffectSubkind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ValueText, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ComputedCharacterItemResult> BuildComputedTraits(
            SqliteConnection connection,
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            var items = new List<ComputedCharacterItemResult>();

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(x => !string.IsNullOrWhiteSpace(x.TargetSemanticKind)
                                     || !string.IsNullOrWhiteSpace(x.TargetSemanticName)))
            {
                string key = grant.TargetSemanticKey ?? grant.TargetSemanticName ?? $"semantic:{grant.GrantId}";
                string name = grant.TargetSemanticName ?? key;
                string typeName = grant.TargetSemanticKind ?? "Semantic Trait";
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "trait",
                        key,
                        "semantic-grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        name,
                        grant.RequirementsText)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "trait",
                    key,
                    name,
                    typeName,
                    grant.TargetPackageKey,
                    false,
                    provenance));
            }

            foreach (ActiveGrantResult grant in evaluation.ActiveGrants
                         .Where(IsComputedTraitGrant))
            {
                string traitCategory = GetGrantTraitCategory(grant);
                string key = grant.TargetAuroraId
                             ?? $"{traitCategory}:{grant.TargetTypeName ?? grant.GrantType}:{grant.TargetName ?? grant.GrantId.ToString(CultureInfo.InvariantCulture)}";
                string name = grant.TargetName
                              ?? grant.TargetSemanticName
                              ?? grant.TargetAuroraId
                              ?? grant.GrantType
                              ?? "Trait";
                string typeName = traitCategory;
                List<CharacterProvenanceEntry> provenance = new()
                {
                    new(
                        "trait",
                        key,
                        "grant",
                        grant.OwnerName,
                        grant.OwnerTypeName,
                        grant.TargetPackageKey,
                        grant.TargetAuroraId,
                        name,
                        grant.RequirementsText ?? grant.GrantType)
                };
                provenanceSink.AddRange(provenance);
                items.Add(new ComputedCharacterItemResult(
                    "trait",
                    key,
                    name,
                    typeName,
                    grant.TargetPackageKey,
                    false,
                    provenance));
            }

            items.AddRange(BuildComputedStatTraits(connection, evaluation, provenanceSink));
            items.AddRange(BuildComputedSetterTraits(connection, evaluation, provenanceSink));

            return MergeComputedItems(items);
        }

        private static bool IsComputedTraitGrant(ActiveGrantResult grant)
        {
            if (!string.IsNullOrWhiteSpace(grant.TargetSemanticKind)
                || !string.IsNullOrWhiteSpace(grant.TargetSemanticName))
            {
                return false;
            }

            return string.Equals(grant.GrantType, "Vision", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(grant.TargetTypeName, "Vision", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetGrantTraitCategory(ActiveGrantResult grant)
        {
            if (string.Equals(grant.GrantType, "Vision", StringComparison.OrdinalIgnoreCase)
                || string.Equals(grant.TargetTypeName, "Vision", StringComparison.OrdinalIgnoreCase))
            {
                return "sense";
            }

            return "trait";
        }

        private static List<ComputedCharacterItemResult> BuildComputedSetterTraits(
            SqliteConnection connection,
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            int[] ownerElementIds = GetComputedTraitOwnerElementIds(evaluation);

            if (ownerElementIds.Length == 0)
                return new List<ComputedCharacterItemResult>();

            string ownerIdList = string.Join(",", ownerElementIds);
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    ss.owner_element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    rec.package_key,
    se.setter_name,
    se.setter_value
FROM setter_scopes AS ss
JOIN elements AS owner
    ON owner.element_id = ss.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = owner.element_id
JOIN setter_entries AS se
    ON se.setter_scope_id = ss.setter_scope_id
WHERE ss.owner_kind = 'element'
  AND ss.owner_element_id IN ({ownerIdList})
  AND se.setter_name IN ('speed', 'vision', 'senses')
ORDER BY ss.owner_element_id ASC, se.setter_name ASC, se.ordinal ASC;";

            var items = new List<ComputedCharacterItemResult>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string setterName = reader.GetString(5);
                string setterValue = reader.IsDBNull(6) ? null : reader.GetString(6);
                if (string.IsNullOrWhiteSpace(setterValue))
                    continue;

                string ownerAuroraId = reader.IsDBNull(1) ? null : reader.GetString(1);
                string ownerName = reader.GetString(2);
                string ownerTypeName = reader.GetString(3);
                string packageKey = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (string.Equals(setterName, "speed", StringComparison.OrdinalIgnoreCase))
                {
                    string movementParserName = ShouldParseCompositeMovement(setterValue)
                        ? null
                        : setterName;

                    foreach (ParsedMovementResult movement in ParseMovementResults(movementParserName, setterValue))
                    {
                        string movementKey = $"movement:{movement.Kind}:{movement.ValueText}";
                        List<CharacterProvenanceEntry> provenance = new()
                        {
                            new(
                                "movement",
                                movementKey,
                                "setter",
                                ownerName,
                                ownerTypeName,
                                packageKey,
                                ownerAuroraId,
                                movement.Label,
                                movement.SourceText)
                        };
                        provenanceSink.AddRange(provenance);
                        items.Add(new ComputedCharacterItemResult(
                            "movement",
                            movementKey,
                            movement.Label,
                            movement.Kind,
                            packageKey,
                            false,
                            provenance));
                    }

                    continue;
                }

                string category = setterName.ToLowerInvariant() switch
                {
                    "vision" => "sense",
                    "senses" => "sense",
                    _ => "trait"
                };
                string displayName = setterName.ToLowerInvariant() switch
                {
                    "vision" => $"Vision: {setterValue}",
                    "senses" => $"Senses: {setterValue}",
                    _ => $"{setterName}: {setterValue}"
                };
                string defaultKey = $"{category}:{setterName}:{setterValue}";
                List<CharacterProvenanceEntry> defaultProvenance = new()
                {
                    new(
                        category,
                        defaultKey,
                        "setter",
                        ownerName,
                        ownerTypeName,
                        packageKey,
                        ownerAuroraId,
                        displayName,
                        $"{setterName}={setterValue}")
                };
                provenanceSink.AddRange(defaultProvenance);
                items.Add(new ComputedCharacterItemResult(
                    category,
                    defaultKey,
                    displayName,
                    setterName,
                    packageKey,
                    false,
                    defaultProvenance));
            }

            return items;
        }

        private static List<ComputedCharacterItemResult> BuildComputedStatTraits(
            SqliteConnection connection,
            CharacterEvaluationResult evaluation,
            List<CharacterProvenanceEntry> provenanceSink)
        {
            int[] ownerElementIds = GetComputedTraitOwnerElementIds(evaluation);

            if (ownerElementIds.Length == 0)
                return new List<ComputedCharacterItemResult>();

            string ownerIdList = string.Join(",", ownerElementIds);
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    rec.package_key,
    st.stat_name,
    st.value_expression_text,
    st.bonus_expression_text,
    st.alt_text
FROM stats AS st
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = st.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN resolved_elements_cache AS rec
    ON rec.winning_element_id = owner.element_id
WHERE rs.owner_kind = 'element'
  AND rs.owner_element_id IN ({ownerIdList})
  AND (
        lower(st.stat_name) = 'speed'
        OR lower(st.stat_name) LIKE '% speed'
        OR lower(st.stat_name) LIKE 'speed %'
        OR lower(st.stat_name) LIKE '%speed:%'
        OR lower(st.stat_name) LIKE '%:speed'
      )
ORDER BY owner.element_id ASC, st.ordinal ASC;";

            var items = new List<ComputedCharacterItemResult>();
            var statRows = new List<(int OwnerElementId, string OwnerAuroraId, string OwnerName, string OwnerTypeName, string PackageKey, string StatName, string ValueExpression, string BonusExpression, string AltText)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    statRows.Add((
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8)));
                }
            }

            Dictionary<string, string> movementAliases = BuildMovementAliasMap(statRows);
            foreach ((int _, string ownerAuroraId, string ownerName, string ownerTypeName, string packageKey, string statName, string valueExpression, string bonusExpression, string altText) in statRows)
            {
                string displayValue = !string.IsNullOrWhiteSpace(altText)
                    ? altText
                    : !string.IsNullOrWhiteSpace(valueExpression)
                        ? valueExpression
                        : bonusExpression;

                displayValue = ResolveMovementAliasValue(displayValue, movementAliases);

                if (string.IsNullOrWhiteSpace(displayValue))
                    continue;

                string normalizedStatName = statName.Trim();
                foreach (ParsedMovementResult movement in ParseMovementResults(normalizedStatName, displayValue))
                {
                    string key = $"movement:{movement.Kind}:{movement.ValueText}";
                    List<CharacterProvenanceEntry> provenance = new()
                    {
                        new(
                            "movement",
                            key,
                            "stat",
                            ownerName,
                            ownerTypeName,
                            packageKey,
                            ownerAuroraId,
                            movement.Label,
                            bonusExpression is { Length: > 0 }
                                ? $"{normalizedStatName} value={valueExpression} bonus={bonusExpression}"
                                : $"{normalizedStatName} value={valueExpression}")
                    };
                    provenanceSink.AddRange(provenance);
                    items.Add(new ComputedCharacterItemResult(
                        "movement",
                        key,
                        movement.Label,
                        movement.Kind,
                        packageKey,
                        false,
                        provenance));
                }
            }

            return items;
        }

        private static Dictionary<string, string> BuildMovementAliasMap(
            IEnumerable<(int OwnerElementId, string OwnerAuroraId, string OwnerName, string OwnerTypeName, string PackageKey, string StatName, string ValueExpression, string BonusExpression, string AltText)> statRows)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach ((int _, string _, string _, string _, string _, string statName, string valueExpression, string bonusExpression, string altText) in statRows)
            {
                string aliasKey = statName?.Trim();
                if (string.IsNullOrWhiteSpace(aliasKey))
                    continue;

                string displayValue = !string.IsNullOrWhiteSpace(altText)
                    ? altText
                    : !string.IsNullOrWhiteSpace(valueExpression)
                        ? valueExpression
                        : bonusExpression;

                if (string.IsNullOrWhiteSpace(displayValue))
                    continue;

                aliases[aliasKey] = displayValue.Trim();
            }

            return aliases;
        }

        private static string ResolveMovementAliasValue(string rawValue, IReadOnlyDictionary<string, string> movementAliases)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return rawValue;

            string current = rawValue.Trim();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (movementAliases.TryGetValue(current, out string nextValue))
            {
                if (string.IsNullOrWhiteSpace(nextValue)
                    || string.Equals(current, nextValue, StringComparison.OrdinalIgnoreCase)
                    || !visited.Add(current))
                {
                    break;
                }

                current = nextValue.Trim();
            }

            return current;
        }

        private static List<ParsedMovementResult> ParseMovementResults(string movementNameOrSetterName, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return new List<ParsedMovementResult>();

            string explicitKind = NormalizeMovementKind(movementNameOrSetterName);
            string sourceText = rawValue.Trim();
            bool shouldParseComposite = ShouldParseCompositeMovement(sourceText);

            if (explicitKind != null && !shouldParseComposite)
            {
                return new List<ParsedMovementResult>
                {
                    BuildMovementResult(explicitKind, sourceText, sourceText)
                };
            }

            var results = new List<ParsedMovementResult>();
            string[] segments = rawValue
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string segment in segments)
            {
                ParsedMovementResult parsed = TryParseMovementSegment(segment);
                if (parsed != null)
                    results.Add(parsed);
            }

            if (results.Count == 0)
            {
                results.Add(BuildMovementResult("walk", sourceText, sourceText));
            }

            return results;
        }

        private static bool ShouldParseCompositeMovement(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            string trimmed = rawValue.Trim();
            if (trimmed.Contains(',') || trimmed.Contains(';'))
                return true;

            return Regex.IsMatch(
                trimmed,
                @"\b(fly|flying|swim|swimming|climb|climbing|burrow|burrowing)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static ParsedMovementResult TryParseMovementSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return null;

            string trimmed = segment.Trim();

            Match leadingTypeMatch = Regex.Match(
                trimmed,
                @"^(?<type>walk|speed|land|ground|fly|flying|swim|swimming|climb|climbing|burrow|burrowing)\s*:?\s*(?<value>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (leadingTypeMatch.Success)
            {
                string kind = NormalizeMovementKind(leadingTypeMatch.Groups["type"].Value);
                string value = leadingTypeMatch.Groups["value"].Value.Trim();
                return BuildMovementResult(kind ?? "walk", value, trimmed);
            }

            Match trailingTypeMatch = Regex.Match(
                trimmed,
                @"^(?<value>.+?)\s+(?<type>fly|flying|swim|swimming|climb|climbing|burrow|burrowing)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (trailingTypeMatch.Success)
            {
                string kind = NormalizeMovementKind(trailingTypeMatch.Groups["type"].Value);
                string value = trailingTypeMatch.Groups["value"].Value.Trim();
                return BuildMovementResult(kind ?? "walk", value, trimmed);
            }

            Match walkValueMatch = Regex.Match(
                trimmed,
                @"^\d+(\s*ft\.?)?(\s*\(.+\))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (walkValueMatch.Success)
            {
                return BuildMovementResult("walk", trimmed, trimmed);
            }

            return null;
        }

        private static ParsedMovementResult BuildMovementResult(string kind, string valueText, string sourceText)
        {
            string normalizedKind = NormalizeMovementKind(kind) ?? "walk";
            string normalizedValueText = NormalizeMovementValueText(valueText);
            string label = normalizedKind switch
            {
                "walk" => "Speed",
                "fly" => "Fly Speed",
                "swim" => "Swim Speed",
                "climb" => "Climb Speed",
                "burrow" => "Burrow Speed",
                _ => $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalizedKind.ToLowerInvariant())} Speed"
            };

            return new ParsedMovementResult(
                normalizedKind,
                $"{label}: {normalizedValueText}",
                normalizedValueText,
                sourceText.Trim());
        }

        private static string NormalizeMovementValueText(string valueText)
        {
            if (string.IsNullOrWhiteSpace(valueText))
                return string.Empty;

            string trimmed = valueText.Trim();
            Match simpleFeetValueMatch = Regex.Match(
                trimmed,
                @"^(?<value>\d+)\s*ft\.?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return simpleFeetValueMatch.Success
                ? simpleFeetValueMatch.Groups["value"].Value
                : trimmed;
        }

        private static string NormalizeMovementKind(string movementName)
        {
            if (string.IsNullOrWhiteSpace(movementName))
                return null;

            string normalized = movementName.Trim().ToLowerInvariant();
            if (normalized.Contains(':'))
            {
                string[] tokens = normalized
                    .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                for (int index = tokens.Length - 1; index >= 0; index--)
                {
                    string tokenKind = NormalizeMovementKind(tokens[index]);
                    if (tokenKind != null)
                        return tokenKind;
                }
            }

            return normalized switch
            {
                "speed" => "walk",
                "innate speed" => "walk",
                "walk" => "walk",
                "walking" => "walk",
                "land" => "walk",
                "ground" => "walk",
                "fly" => "fly",
                "flying" => "fly",
                "fly speed" => "fly",
                "swim" => "swim",
                "swimming" => "swim",
                "swim speed" => "swim",
                "climb" => "climb",
                "climbing" => "climb",
                "climb speed" => "climb",
                "burrow" => "burrow",
                "burrowing" => "burrow",
                "burrow speed" => "burrow",
                _ when normalized.EndsWith(" speed", StringComparison.OrdinalIgnoreCase)
                    => NormalizeMovementKind(normalized[..^" speed".Length]),
                _ => null
            };
        }

        private static int[] GetComputedTraitOwnerElementIds(CharacterEvaluationResult evaluation)
        {
            return evaluation.DirectSelections
                .Select(x => x.ElementId)
                .Concat(evaluation.ActiveFeatures.Select(x => x.ElementId))
                .Concat(evaluation.ActiveGrants
                    .Where(x => x.TargetElementId.HasValue)
                    .Select(x => x.TargetElementId!.Value))
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
        }

        private static List<ComputedCharacterItemResult> MergeComputedItems(IEnumerable<ComputedCharacterItemResult> items)
        {
            return items
                .GroupBy(GetComputedItemMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    ComputedCharacterItemResult first = NormalizeComputedItemForMerge(group.First());
                    IReadOnlyList<CharacterProvenanceEntry> provenance = group
                        .SelectMany(x => x.Provenance)
                        .Distinct()
                        .OrderBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ComputedCharacterItemResult(
                        first.Category,
                        first.Key,
                        first.Name,
                        first.TypeName,
                        first.PackageKey,
                        group.Any(x => x.IsDirectSelection),
                        provenance);
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PackageKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetComputedItemMergeKey(ComputedCharacterItemResult item)
        {
            ComputedCharacterItemResult normalized = NormalizeComputedItemForMerge(item);
            return $"{normalized.Category}|{normalized.Key}";
        }

        private static ComputedCharacterItemResult NormalizeComputedItemForMerge(ComputedCharacterItemResult item)
        {
            if (!string.Equals(item.Category, "movement", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.TypeName)
                || string.IsNullOrWhiteSpace(item.Key)
                || !item.Key.StartsWith("movement:", StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            int firstSeparatorIndex = item.Key.IndexOf(':');
            int secondSeparatorIndex = firstSeparatorIndex >= 0
                ? item.Key.IndexOf(':', firstSeparatorIndex + 1)
                : -1;

            if (secondSeparatorIndex < 0 || secondSeparatorIndex >= item.Key.Length - 1)
                return item;

            string kind = item.Key[(firstSeparatorIndex + 1)..secondSeparatorIndex];
            string valueText = item.Key[(secondSeparatorIndex + 1)..];
            ParsedMovementResult normalizedMovement = BuildMovementResult(kind, valueText, valueText);
            string normalizedKey = $"movement:{normalizedMovement.Kind}:{normalizedMovement.ValueText}";

            if (string.Equals(normalizedKey, item.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedMovement.Label, item.Name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            return new ComputedCharacterItemResult(
                item.Category,
                normalizedKey,
                normalizedMovement.Label,
                normalizedMovement.Kind,
                item.PackageKey,
                item.IsDirectSelection,
                item.Provenance);
        }

        private static List<PendingCharacterChoiceResult> BuildPendingChoices(
            SqliteConnection connection,
            IReadOnlyList<CharacterSelectResult> availableSelects,
            IReadOnlyList<AppliedCharacterChoiceResult> appliedChoices,
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            AuroraCharacterStateDocument workingDocument)
        {
            return availableSelects
                .Select(select =>
                {
                    int chosenCount = CountSatisfiedChoicesForSelect(
                        connection,
                        select,
                        appliedChoices,
                        directSelections,
                        workingDocument);
                    int alreadyOwnedCount = select.Options.Count(x => x.IsAlreadyOwned);
                    int remainingCount = Math.Max(0, select.NumberToChoose - chosenCount);
                    int availableOptionCount = select.Options.Count(x => x.IsAvailable && !x.IsAlreadyOwned);
                    return new PendingCharacterChoiceResult(
                        select.SelectId,
                        select.OwnerName,
                        select.OwnerTypeName,
                        select.OwnerPackageKey,
                        select.SelectName,
                        select.SelectType,
                        select.SelectPolicy,
                        select.ChoiceFamily,
                        select.NumberToChoose,
                        chosenCount,
                        alreadyOwnedCount,
                        remainingCount,
                        availableOptionCount,
                        select.IsOptional,
                        remainingCount > 0 && !select.IsOptional);
                })
                .Where(x => x.RemainingCount > 0)
                .OrderByDescending(x => x.IsBlocking)
                .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SelectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int CountSatisfiedChoicesForSelect(
            SqliteConnection connection,
            CharacterSelectResult select,
            IReadOnlyList<AppliedCharacterChoiceResult> appliedChoices,
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            AuroraCharacterStateDocument workingDocument)
        {
            if (select == null)
                return 0;

            var satisfiedChoiceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AppliedCharacterChoiceResult choice in appliedChoices ?? Array.Empty<AppliedCharacterChoiceResult>())
            {
                if (!string.Equals(choice.Status, "applied", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(choice.Status, "already-applied", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (choice.SelectId != select.SelectId)
                    continue;

                satisfiedChoiceKeys.Add(BuildAppliedChoiceKey(choice));
            }

            string macroName = BuildChoiceMacroName(select.OwnerTypeName, select.OwnerName, select.SelectName);
            if (workingDocument?.MacroValues != null
                && workingDocument.MacroValues.TryGetValue(macroName, out List<string> storedValues)
                && storedValues != null)
            {
                foreach (string storedValue in storedValues)
                {
                    string trimmed = storedValue?.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        satisfiedChoiceKeys.Add($"stored:{trimmed}");
                }
            }

            foreach (ResolvedCharacterElement selection in directSelections ?? Array.Empty<ResolvedCharacterElement>())
            {
                if (!DoesDirectSelectionSatisfySelect(connection, select, selection))
                    continue;

                string key = !string.IsNullOrWhiteSpace(selection.AuroraId)
                    ? selection.AuroraId
                    : selection.Name;

                if (!string.IsNullOrWhiteSpace(key))
                    satisfiedChoiceKeys.Add($"selection:{key.Trim()}");
            }

            return satisfiedChoiceKeys.Count;
        }

        private static string BuildAppliedChoiceKey(AppliedCharacterChoiceResult choice)
        {
            return $"{choice.OptionAuroraId ?? string.Empty}|{choice.OptionName ?? string.Empty}|{choice.FollowUpOptionAuroraId ?? string.Empty}|{choice.FollowUpOptionName ?? string.Empty}";
        }

        private static bool DoesDirectSelectionSatisfySelect(
            SqliteConnection connection,
            CharacterSelectResult select,
            ResolvedCharacterElement selection)
        {
            if (select == null || selection == null)
                return false;

            if (select.Options != null && select.Options.Count > 0)
            {
                if (select.Options.Any(option => DoesOptionMatchDirectSelection(option, selection)))
                    return true;
            }

            if (!OptionMatchesSelectType(select.SelectType, selection.TypeName))
                return false;

            bool specializedMatch = select.SelectType?.Trim() switch
            {
                "Archetype" => DoesArchetypeSelectionMatchSelect(connection, select, selection),
                "Race Variant" => DoesRaceVariantSelectionMatchSelect(connection, select, selection),
                "Sub Race" => DoesSubRaceSelectionMatchSelect(connection, select, selection),
                _ => SupportsContainAny(select.SupportsText, selection.AuroraId, selection.Name)
            };

            if (specializedMatch)
                return true;

            return SupportsContainAny(select.SupportsText, selection.AuroraId, selection.Name, selection.TypeName);
        }

        private static bool DoesOptionMatchDirectSelection(
            CharacterSelectOptionResult option,
            ResolvedCharacterElement selection)
        {
            if (option == null || selection == null)
                return false;

            if (option.OptionElementId.HasValue && option.OptionElementId.Value == selection.ElementId)
                return true;

            if (!string.IsNullOrWhiteSpace(option.OptionAuroraId)
                && !string.IsNullOrWhiteSpace(selection.AuroraId)
                && string.Equals(option.OptionAuroraId, selection.AuroraId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(option.OptionName)
                   && string.Equals(option.OptionName, selection.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool DoesArchetypeSelectionMatchSelect(
            SqliteConnection connection,
            CharacterSelectResult select,
            ResolvedCharacterElement selection)
        {
            if (!string.Equals(selection.TypeName, "Archetype", StringComparison.OrdinalIgnoreCase))
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    a.parent_support_text,
    pe.aurora_id,
    pe.name
FROM archetypes AS a
LEFT JOIN elements AS pe
    ON pe.element_id = a.parent_class_element_id
WHERE a.element_id = $element_id;";
            command.Parameters.AddWithValue("$element_id", selection.ElementId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return false;

            string parentSupportText = reader.IsDBNull(0) ? null : reader.GetString(0);
            string parentAuroraId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string parentName = reader.IsDBNull(2) ? null : reader.GetString(2);

            return SupportsContainAny(select.SupportsText, parentSupportText, parentAuroraId, parentName);
        }

        private static bool DoesRaceVariantSelectionMatchSelect(
            SqliteConnection connection,
            CharacterSelectResult select,
            ResolvedCharacterElement selection)
        {
            if (!string.Equals(selection.TypeName, "Race Variant", StringComparison.OrdinalIgnoreCase))
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    rv.parent_support_text,
    pe.aurora_id,
    pe.name
FROM race_variants AS rv
LEFT JOIN elements AS pe
    ON pe.element_id = rv.race_element_id
WHERE rv.element_id = $element_id;";
            command.Parameters.AddWithValue("$element_id", selection.ElementId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return false;

            string parentSupportText = reader.IsDBNull(0) ? null : reader.GetString(0);
            string parentAuroraId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string parentName = reader.IsDBNull(2) ? null : reader.GetString(2);

            return SupportsContainAny(select.SupportsText, parentSupportText, parentAuroraId, parentName);
        }

        private static bool DoesSubRaceSelectionMatchSelect(
            SqliteConnection connection,
            CharacterSelectResult select,
            ResolvedCharacterElement selection)
        {
            if (!string.Equals(selection.TypeName, "Sub Race", StringComparison.OrdinalIgnoreCase))
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    sr.parent_support_text,
    pe.aurora_id,
    pe.name
FROM subraces AS sr
LEFT JOIN elements AS pe
    ON pe.element_id = sr.race_element_id
WHERE sr.element_id = $element_id;";
            command.Parameters.AddWithValue("$element_id", selection.ElementId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return false;

            string parentSupportText = reader.IsDBNull(0) ? null : reader.GetString(0);
            string parentAuroraId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string parentName = reader.IsDBNull(2) ? null : reader.GetString(2);

            return SupportsContainAny(select.SupportsText, parentSupportText, parentAuroraId, parentName);
        }

        private static bool SupportsContainAny(string supportsText, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(supportsText) || candidates == null || candidates.Length == 0)
                return false;

            List<string> supportAtoms = ExtractSupportAtoms(supportsText);
            if (supportAtoms.Count == 0)
                return false;

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (supportAtoms.Any(atom => string.Equals(atom, candidate.Trim(), StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private static List<CharacterWarningResult> BuildCharacterWarnings(
            SqliteConnection connection,
            IReadOnlyList<AppliedCharacterChoiceResult> appliedChoices,
            IReadOnlyList<PendingCharacterChoiceResult> pendingChoices,
            IReadOnlyList<CharacterSelectResult> availableSelects,
            IReadOnlyList<ResolvedCharacterElement> directSelections,
            AuroraCharacterStateDocument workingDocument)
        {
            var warnings = new List<CharacterWarningResult>();

            foreach (AppliedCharacterChoiceResult choice in appliedChoices
                         .Where(x => !string.Equals(x.Status, "applied", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(x.Status, "already-applied", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new CharacterWarningResult(
                    "choice-application",
                    string.Equals(choice.Status, "blocked", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                    choice.Message ?? $"Choice status was {choice.Status}.",
                    choice.OwnerName,
                    choice.OwnerTypeName,
                    choice.SelectName));
            }

            foreach (PendingCharacterChoiceResult pendingChoice in pendingChoices.Where(x => x.IsBlocking))
            {
                string severity = pendingChoice.AvailableOptionCount == 0 ? "error" : "warning";
                string message = pendingChoice.AvailableOptionCount == 0
                    ? "This required choice has no currently available options."
                    : $"This required choice still needs {pendingChoice.RemainingCount} selection(s).";

                warnings.Add(new CharacterWarningResult(
                    "pending-choice",
                    severity,
                    message,
                    pendingChoice.OwnerName,
                    pendingChoice.OwnerTypeName,
                    pendingChoice.SelectName));
            }

            foreach (CharacterSelectResult select in availableSelects ?? Array.Empty<CharacterSelectResult>())
            {
                int satisfiedCount = CountSatisfiedChoicesForSelect(
                    connection,
                    select,
                    appliedChoices,
                    directSelections,
                    workingDocument);
                if (satisfiedCount <= select.NumberToChoose)
                    continue;

                warnings.Add(new CharacterWarningResult(
                    "over-selected-choice",
                    "error",
                    $"This choice has {satisfiedCount} selections but only allows {select.NumberToChoose}.",
                    select.OwnerName,
                    select.OwnerTypeName,
                    select.SelectName));
            }

            return warnings
                .OrderByDescending(x => string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.WarningKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ClassifySelectPolicy(string selectType, string selectName, string supportsText)
        {
            selectType = selectType?.Trim();
            selectName = selectName?.Trim();

            if (string.Equals(selectType, "Language", StringComparison.OrdinalIgnoreCase))
                return "broad-language-pool";

            if (string.Equals(selectType, "Proficiency", StringComparison.OrdinalIgnoreCase))
                return "broad-proficiency-pool";

            if (string.Equals(selectType, "Feat", StringComparison.OrdinalIgnoreCase))
                return "broad-feat-pool";

            if (string.Equals(selectType, "List", StringComparison.OrdinalIgnoreCase))
                return "text-choice-pool";

            if (string.Equals(selectType, "Class Feature", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(supportsText)
                && supportsText.Contains("Improvement Option", StringComparison.OrdinalIgnoreCase))
            {
                return "asi-feature-pool";
            }

            return "fixed-element-pool";
        }

        private static string ClassifyChoiceFamily(
            string selectType,
            string selectPolicy,
            string selectName,
            string supportsText)
        {
            selectType = selectType?.Trim();
            selectName = selectName?.Trim();
            supportsText = supportsText?.Trim();

            if (string.Equals(selectPolicy, "asi-feature-pool", StringComparison.OrdinalIgnoreCase))
                return "asi-pick";

            if (string.Equals(selectType, "Language", StringComparison.OrdinalIgnoreCase))
                return "language-pick";

            if (string.Equals(selectType, "Proficiency", StringComparison.OrdinalIgnoreCase))
            {
                string family = ClassifyProficiencyChoiceFamily(selectName, supportsText);
                return family ?? "proficiency-pick";
            }

            if (string.Equals(selectType, "Feat", StringComparison.OrdinalIgnoreCase))
                return "feat-pick";

            if (string.Equals(selectType, "List", StringComparison.OrdinalIgnoreCase))
                return "text-choice";

            if (string.Equals(selectType, "Race Variant", StringComparison.OrdinalIgnoreCase))
                return "race-variant-pick";

            if (string.Equals(selectType, "Class Feature", StringComparison.OrdinalIgnoreCase))
            {
                if ((selectName?.Contains("Fighting Style", StringComparison.OrdinalIgnoreCase) ?? false)
                    || (supportsText?.Contains("Fighting Style", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return "fighting-style-pick";
                }

                return "feature-pick";
            }

            return "generic-element-pick";
        }

        private static string ClassifyProficiencyChoiceFamily(string selectName, string supportsText)
        {
            string combined = string.Join(" | ", new[] { selectName, supportsText }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (combined.Contains("Skill", StringComparison.OrdinalIgnoreCase))
                return "skill-pick";
            if (combined.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                return "tool-pick";
            if (combined.Contains("Armor", StringComparison.OrdinalIgnoreCase))
                return "armor-proficiency-pick";
            if (combined.Contains("Weapon", StringComparison.OrdinalIgnoreCase))
                return "weapon-proficiency-pick";
            if (combined.Contains("Saving Throw", StringComparison.OrdinalIgnoreCase))
                return "saving-throw-pick";

            return null;
        }

        private static bool IsMovementKind(string movementName)
        {
            return NormalizeMovementKind(movementName) != null;
        }

        private static decimal GetAbilityScore(AuroraExpressionEvaluationContext context, string abilityKey)
        {
            if (context.NumericValues.TryGetValue(abilityKey, out decimal value))
                return value;

            return 0m;
        }

        private static AuroraExpressionEvaluationContext CloneContext(AuroraExpressionEvaluationContext source)
        {
            var clone = new AuroraExpressionEvaluationContext();

            foreach (string token in source.Tokens)
                clone.AddToken(token);

            foreach (KeyValuePair<string, decimal> pair in source.NumericValues)
                clone.AddNumericValue(pair.Key, pair.Value);

            foreach (KeyValuePair<string, string> pair in source.ScalarValues)
                clone.AddScalarValue(pair.Key, pair.Value);

            foreach (KeyValuePair<string, HashSet<string>> pair in source.MacroValues)
                clone.AddMacroValues(pair.Key, pair.Value);

            return clone;
        }

        private static string GetAbilityDisplayName(string abilityKey)
        {
            return abilityKey?.ToLowerInvariant() switch
            {
                "str" => "Strength",
                "dex" => "Dexterity",
                "con" => "Constitution",
                "int" => "Intelligence",
                "wis" => "Wisdom",
                "cha" => "Charisma",
                _ => abilityKey ?? "Ability"
            };
        }

        private static string BuildAsiPayload(string mode, params string[] abilities)
        {
            return JsonSerializer.Serialize(new
            {
                mode,
                abilities
            });
        }

        private static string LoadElementRequirementText(SqliteConnection connection, int elementId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT requirement_text
FROM element_requirements
WHERE element_id = $element_id
ORDER BY ordinal ASC;";
            command.Parameters.AddWithValue("$element_id", elementId);

            var requirements = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string requirement = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(requirement))
                    requirements.Add(requirement.Trim());
            }

            return requirements.Count == 0 ? null : string.Join(",", requirements);
        }

        private static bool IsRequirementSatisfied(string requirementsText, AuroraExpressionEvaluationContext context)
        {
            if (string.IsNullOrWhiteSpace(requirementsText))
                return true;

            return AuroraExpressionEngine.Evaluate(requirementsText, context);
        }
    }
}
