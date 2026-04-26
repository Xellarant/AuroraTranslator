using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        string RequirementText);

    internal sealed record CharacterSelectResult(
        int SelectId,
        string OwnerName,
        string OwnerTypeName,
        string OwnerPackageKey,
        string SelectName,
        string SelectType,
        string SelectPolicy,
        string SupportsText,
        int? SelectLevel,
        int NumberToChoose,
        bool IsOptional,
        string RequirementsText,
        IReadOnlyList<CharacterSelectOptionResult> Options);

    internal sealed record CharacterEvaluationResult(
        IReadOnlyList<ResolvedCharacterElement> DirectSelections,
        IReadOnlyList<ActiveCharacterFeature> ActiveFeatures,
        IReadOnlyList<ActiveGrantResult> ActiveGrants,
        IReadOnlyList<CharacterSelectResult> AvailableSelects,
        AuroraExpressionEvaluationContext EvaluationContext);

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
            return new CharacterEvaluationResult(directSelections, activeFeatures, activeGrants, availableSelects, evaluationContext);
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
                    requirementsText,
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14)));
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
                List<CharacterSelectOptionResult> options = LoadSelectOptions(connection, selectId, selectType, selectPolicy, supportsText, context);
                selects.Add(new CharacterSelectResult(
                    selectId,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    selectType,
                    selectPolicy,
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
            AuroraExpressionEvaluationContext context)
        {
            if (string.Equals(selectPolicy, "broad-language-pool", StringComparison.OrdinalIgnoreCase))
                return LoadLanguageOptions(connection, supportsText, context);

            if (string.Equals(selectPolicy, "broad-proficiency-pool", StringComparison.OrdinalIgnoreCase))
                return LoadProficiencyOptions(connection, supportsText, context);

            if (string.Equals(selectPolicy, "asi-feature-pool", StringComparison.OrdinalIgnoreCase))
                return LoadAsiFeatureOptions(connection, selectId, supportsText, context);

            return LoadGenericSelectableOptions(connection, selectId, selectType, context);
        }

        private static List<CharacterSelectOptionResult> LoadGenericSelectableOptions(
            SqliteConnection connection,
            int selectId,
            string selectType,
            AuroraExpressionEvaluationContext context)
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

                if (optionElementId.HasValue)
                {
                    if (!OptionMatchesSelectType(selectType, optionTypeName))
                        continue;

                    requirementText = LoadElementRequirementText(connection, optionElementId.Value);
                    isAvailable = IsRequirementSatisfied(requirementText, context);
                    isAlreadyOwned = (!string.IsNullOrWhiteSpace(optionAuroraId) && context.MatchesToken(optionAuroraId))
                                     || (!string.IsNullOrWhiteSpace(optionName) && context.MatchesToken(optionName));
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
                    requirementText));
            }

            return options
                .GroupBy(x => $"{x.OptionKind}|{x.OptionElementId?.ToString() ?? ""}|{x.OptionText ?? ""}")
                .Select(x => x.First())
                .ToList();
        }

        private static bool OptionMatchesSelectType(string selectType, string optionTypeName)
        {
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
            AuroraExpressionEvaluationContext context)
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
                    null),
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
                    "ID_INTERNAL_OPTION_ALLOW_FEATS")
            };
        }

        private static List<CharacterSelectOptionResult> LoadProficiencyOptions(
            SqliteConnection connection,
            string supportsText,
            AuroraExpressionEvaluationContext context)
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

        private static string ClassifySelectPolicy(string selectType, string selectName, string supportsText)
        {
            if (string.Equals(selectType, "Language", StringComparison.OrdinalIgnoreCase))
                return "broad-language-pool";

            if (string.Equals(selectType, "Proficiency", StringComparison.OrdinalIgnoreCase))
                return "broad-proficiency-pool";

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
