using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

internal static class AuroraSelectionRules
{
    internal sealed record SupportOption(int Id, string Type, AuroraExpressionEvaluationContext Context);

    internal static bool IsStaticSupportExpression(AuroraExpressionNode node)
        => node.Kind == "value" ? node.ValueType is "text" or "aurora-id" : node.Children.All(IsStaticSupportExpression);

    // These pools have separate spell-list, proficiency, feat or text semantics.
    internal static bool UsesGenericElementPool(string type)
        => type?.Trim().ToLowerInvariant() is not ("spell" or "language" or "proficiency" or "feat" or "list");

    internal static IReadOnlyDictionary<int, SupportOption> LoadSupportOptions(SqliteConnection connection, SqliteTransaction transaction = null)
    {
        var options = new Dictionary<int, SupportOption>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT e.element_id,et.type_name,e.aurora_id,es.support_text
            FROM resolved_elements_cache r
            JOIN elements e ON e.element_id=r.winning_element_id
            JOIN element_types et ON et.element_type_id=e.element_type_id
            LEFT JOIN element_supports es ON es.element_id=e.element_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            if (!options.TryGetValue(id, out var option))
            {
                option = new(id, reader.GetString(1), new AuroraExpressionEvaluationContext());
                if (!reader.IsDBNull(2)) option.Context.AddToken(reader.GetString(2));
                options.Add(id, option);
            }
            if (!reader.IsDBNull(3)) option.Context.AddToken(reader.GetString(3));
        }
        return options;
    }

    internal static bool Matches(AuroraExpressionParseResult expression, SupportOption option)
        => expression?.Status == "parsed" && option != null && AuroraExpressionEngine.Evaluate(expression.RootNode, option.Context);

    internal static bool Matches(AuroraExpressionParseResult expression, SupportOption option, AuroraExpressionEvaluationContext character)
        => expression?.Status == "parsed" && option != null
            && AuroraExpressionEngine.EvaluateWithValues(expression.RootNode, node => MatchValue(node, option.Context, character)) == true;

    internal static bool? MatchValue(AuroraExpressionNode node, AuroraExpressionEvaluationContext option, AuroraExpressionEvaluationContext character)
    {
        if (node.ValueType == "macro")
            return character != null && character.MacroValues.TryGetValue(node.ValueText, out var values)
                ? values.Overlaps(option.Tokens) : null;
        if (node.ValueType == "bracket")
        {
            string inner = node.ValueText.Trim('[', ']');
            int separator = inner.LastIndexOf(':');
            string key = separator < 0 ? inner : inner[..separator];
            return character != null && (character.NumericValues.ContainsKey(key) || character.ScalarValues.ContainsKey(key)
                || character.Tokens.Contains(inner) || character.Tokens.Contains(node.ValueText))
                ? character.EvaluateBracket(node.ValueText) : null;
        }
        return option.MatchesToken(node.ValueText);
    }

    internal static bool OptionMatchesSelectType(string selectType, string optionTypeName)
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
}
