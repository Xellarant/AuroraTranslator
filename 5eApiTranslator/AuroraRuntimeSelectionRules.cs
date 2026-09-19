using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace AuroraTranslator;

internal static partial class AuroraCharacterStateEngine
{
    private static AuroraExpressionParseResult ParseSelectionSupports(string text, AuroraExpressionEvaluationContext context, string owner, string select)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var expression = AuroraExpressionEngine.Parse(text);
        if (expression.Status != "parsed")
            SelectionWarning(context, "invalid-support-expression", $"Correct supports '{text}': {expression.ErrorText}", owner, select);
        else
            foreach (var value in SupportValues(expression.RootNode))
                if (value.ValueType is "macro" or "bracket"
                    && AuroraSelectionRules.MatchValue(value, AuroraExpressionEvaluationContext.Empty, context) == null)
                    SelectionWarning(context, "unresolved-support-binding", $"Supply a character binding for {value.ValueText} in supports '{text}'.", owner, select);
        return expression;
    }

    private static IEnumerable<AuroraExpressionNode> SupportValues(AuroraExpressionNode node)
        => node.Kind == "value" ? new[] { node } : node.Children.SelectMany(SupportValues);

    private static void SelectionWarning(AuroraExpressionEvaluationContext context, string kind, string message, string owner, string select)
    {
        var warning = new CharacterWarningResult(kind, "warning", message, owner, null, select);
        if (!context.SelectionWarnings.Contains(warning)) context.SelectionWarnings.Add(warning);
    }

    private static bool MatchesSelectionSupports(SqliteConnection connection, AuroraExpressionParseResult expression, int elementId, AuroraExpressionEvaluationContext context)
        => expression == null || (SupportOptions.GetValue(connection, c => AuroraSelectionRules.LoadSupportOptions(c)).TryGetValue(elementId, out var option)
            && AuroraSelectionRules.Matches(expression, option, context));

    private static void BindActiveOwnership(SqliteConnection connection, AuroraExpressionEvaluationContext context,
        IReadOnlyList<ResolvedCharacterElement> selections, IReadOnlyList<ActiveGrantResult> grants, IReadOnlyList<AppliedCharacterChoiceResult> choices)
    {
        foreach (var selection in selections.Where(s => s.TypeName == "Class"))
            context.ActiveClasses[selection.ElementId] = (selection.AuroraId, selection.Name);
        foreach (var grant in grants.Where(g => g.TargetElementId.HasValue))
            AddActiveParent(context, grant.TargetElementId.Value, grant.OwnerElementId);
        foreach (var choice in choices.Where(c => c.Status is "applied" or "already-applied" && c.SelectId.HasValue && c.OptionAuroraId != null))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT e.element_id,rs.owner_element_id,followup.winning_element_id
                FROM selects s JOIN rule_scopes rs ON rs.rule_scope_id=s.rule_scope_id
                JOIN resolved_elements_cache r ON r.aurora_id=$option
                JOIN elements e ON e.element_id=r.winning_element_id
                LEFT JOIN resolved_elements_cache followup ON followup.aurora_id=$followup
                WHERE s.select_id=$select AND rs.owner_kind='element';
                """;
            command.Parameters.AddWithValue("$select", choice.SelectId.Value);
            command.Parameters.AddWithValue("$option", choice.OptionAuroraId);
            command.Parameters.AddWithValue("$followup", (object)choice.FollowUpOptionAuroraId ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                AddActiveParent(context, reader.GetInt32(0), reader.GetInt32(1));
                if (!reader.IsDBNull(2)) AddActiveParent(context, reader.GetInt32(2), reader.GetInt32(0));
            }
        }
        // Direct subclass references can carry an explicit class ID in supports.
        // Family matches and unchosen selector candidates do not establish ownership.
        foreach (var selection in selections)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT r.winning_element_id FROM element_supports es JOIN resolved_elements_cache r ON r.aurora_id=es.support_text WHERE es.element_id=$id;";
            command.Parameters.AddWithValue("$id", selection.ElementId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (context.ActiveClasses.ContainsKey(reader.GetInt32(0))) AddActiveParent(context, selection.ElementId, reader.GetInt32(0));
        }
    }

    private static void AddActiveParent(AuroraExpressionEvaluationContext context, int child, int parent)
    {
        if (child == parent) return;
        if (!context.ActiveParents.TryGetValue(child, out var parents)) context.ActiveParents[child] = parents = new();
        parents.Add(parent);
    }

    private static HashSet<int> ActiveClassOwners(AuroraExpressionEvaluationContext context, int owner)
    {
        var found = new HashSet<int>();
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(owner);
        while (pending.TryPop(out int current))
        {
            if (!visited.Add(current)) continue;
            if (context.ActiveClasses.ContainsKey(current)) { found.Add(current); continue; }
            if (context.ActiveParents.TryGetValue(current, out var parents)) foreach (int parent in parents) pending.Push(parent);
        }
        return found;
    }

    private static (string Name, string List) ResolveImplicitSpellcasting(SqliteConnection connection, int owner,
        AuroraExpressionEvaluationContext context, string ownerName, string selectName)
    {
        var classes = ActiveClassOwners(context, owner);
        if (classes.Count != 1)
        {
            string candidates = string.Join(", ", classes.Select(id => context.ActiveClasses[id].Id).Order());
            SelectionWarning(context, "unresolved-spellcasting-owner",
                $"Spell selector on {ownerName} needs an explicit spellcasting profile or a unique active owner. Active class candidates: {(candidates.Length == 0 ? "none" : candidates)}.", ownerName, selectName);
            return (null, null);
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.owner_element_id,p.profile_name,p.list_text
            FROM spellcasting_profiles p JOIN resolved_elements_cache r ON r.winning_element_id=p.owner_element_id
            WHERE COALESCE(p.is_extended,0)=0 AND p.list_text IS NOT NULL;
            """;
        var profiles = new List<(int Owner, string Name, string List)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int candidate = reader.GetInt32(0);
            if (ActiveClassOwners(context, candidate).SetEquals(classes))
                profiles.Add((candidate, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        }
        if (profiles.Count == 1) return (profiles[0].Name, profiles[0].List);
        SelectionWarning(context, "unresolved-spellcasting-profile",
            $"Spell selector on {ownerName} has {profiles.Count} active spellcasting profiles for {context.ActiveClasses[classes.Single()].Id}. Declare its spellcasting profile explicitly.", ownerName, selectName);
        return (null, null);
    }
}
