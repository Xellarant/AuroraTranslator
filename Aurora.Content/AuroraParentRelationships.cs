using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

internal static partial class AuroraSqliteImporter
{
    // Structural relationships, not character-specific availability. Multiple
    // selectors/granters can legitimately share an option.
    private static void ResolveParentRelationships(SqliteConnection connection, SqliteTransaction transaction, bool affectedScopeOnly)
    {
        ExecuteSql(connection, transaction, """
            CREATE TABLE IF NOT EXISTS parent_relationship_candidates (
              owner_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
              parent_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
              link_kind TEXT NOT NULL, resolution_kind TEXT NOT NULL, evidence_rank INTEGER NOT NULL,
              PRIMARY KEY(owner_element_id,parent_element_id,link_kind));
            CREATE TABLE IF NOT EXISTS parent_selector_diagnostics (
              select_id INTEGER PRIMARY KEY REFERENCES selects(select_id) ON DELETE CASCADE,
              diagnostic_status TEXT NOT NULL, diagnostic_reason TEXT NOT NULL, diagnostic_text TEXT NOT NULL);
            CREATE TEMP TABLE IF NOT EXISTS parent_owners (
              element_id INTEGER PRIMARY KEY, link_kind TEXT NOT NULL, required_parent_type TEXT);
            CREATE TEMP TABLE IF NOT EXISTS parent_rule_matches (
              child_id INTEGER NOT NULL, parent_id INTEGER NOT NULL, resolution_kind TEXT NOT NULL,
              PRIMARY KEY(child_id,parent_id,resolution_kind));
            DELETE FROM temp.parent_owners;
            DELETE FROM temp.parent_rule_matches;
            DELETE FROM parent_relationship_candidates;
            DELETE FROM parent_selector_diagnostics;
            INSERT INTO temp.parent_owners SELECT element_id,'feature-parent',NULL FROM features;
            INSERT INTO temp.parent_owners SELECT element_id,'archetype-parent','Class' FROM archetypes;
            INSERT INTO temp.parent_owners SELECT element_id,'subrace-parent','Race' FROM subraces;
            INSERT INTO temp.parent_owners SELECT element_id,'race-variant-parent','Race' FROM race_variants;
            INSERT INTO temp.parent_owners SELECT element_id,'background-variant-parent','Background' FROM background_variants;
            """);

        // A changed grant ancestor can affect a selector several edges away.
        // Rebuild this derived graph conservatively until dependency indexing exists.
        if (affectedScopeOnly)
            ExecuteSql(connection, transaction, "INSERT OR IGNORE INTO temp.affected_owner_elements SELECT element_id FROM temp.parent_owners;");

        ExecuteSql(connection, transaction, """
            INSERT OR IGNORE INTO temp.parent_rule_matches
            SELECT o.element_id,p.element_id,'aurora-id'
            FROM temp.parent_owners o
            JOIN element_supports es ON es.element_id=o.element_id
            JOIN elements p ON p.aurora_id=es.support_text AND p.element_id<>o.element_id
            JOIN resolved_elements_cache rec ON rec.winning_element_id=p.element_id;

            INSERT OR IGNORE INTO temp.parent_rule_matches
            SELECT o.element_id,rs.owner_element_id,'grant-id'
            FROM temp.parent_owners o
            JOIN elements child ON child.element_id=o.element_id
            JOIN grants g ON g.target_aurora_id=child.aurora_id
            JOIN rule_scopes rs ON rs.rule_scope_id=g.rule_scope_id AND rs.owner_kind='element'
            JOIN resolved_elements_cache rec ON rec.winning_element_id=rs.owner_element_id
            WHERE o.element_id<>rs.owner_element_id;
            """);

        AddSelectorParentMatches(connection, transaction);

        // A subclass selector can be a granted Class Feature. Follow those explicit
        // grant IDs to a Class (likewise Race/Background), never a name alias. UNION
        // deduplicates visited edges so cyclic grant declarations terminate.
        ExecuteSql(connection, transaction, """
            WITH RECURSIVE ancestry(child_id,parent_id,resolution_kind) AS (
              SELECT child_id,parent_id,resolution_kind FROM temp.parent_rule_matches
              UNION
              SELECT a.child_id,rs.owner_element_id,a.resolution_kind
              FROM ancestry a
              JOIN temp.parent_owners o ON o.element_id=a.child_id
              JOIN elements p ON p.element_id=a.parent_id
              JOIN element_types pt ON pt.element_type_id=p.element_type_id
              JOIN grants g ON g.target_aurora_id=p.aurora_id
              JOIN rule_scopes rs ON rs.rule_scope_id=g.rule_scope_id AND rs.owner_kind='element'
              JOIN resolved_elements_cache rec ON rec.winning_element_id=rs.owner_element_id
              WHERE o.required_parent_type IS NOT NULL AND pt.type_name<>o.required_parent_type
                AND rs.owner_element_id<>a.child_id
            )
            INSERT INTO parent_relationship_candidates
            SELECT a.child_id,a.parent_id,o.link_kind,group_concat(DISTINCT a.resolution_kind),1
            FROM ancestry a
            JOIN temp.parent_owners o ON o.element_id=a.child_id
            JOIN elements p ON p.element_id=a.parent_id
            JOIN element_types pt ON pt.element_type_id=p.element_type_id
            WHERE o.required_parent_type IS NULL OR pt.type_name=o.required_parent_type
            GROUP BY a.child_id,a.parent_id,o.link_kind;
            """);

        foreach (var (table, column) in new[] { ("features", "parent_element_id"), ("archetypes", "parent_class_element_id"),
            ("subraces", "race_element_id"), ("race_variants", "race_element_id"), ("background_variants", "background_element_id") })
            ExecuteSql(connection, transaction, $"""
                UPDATE {table} SET {column}=(
                  SELECT CASE WHEN COUNT(*)=1 THEN MIN(c.parent_element_id) END
                  FROM parent_relationship_candidates c WHERE c.owner_element_id={table}.element_id);
                """);
        ExecuteSql(connection, transaction, """
            CREATE VIEW IF NOT EXISTS v_ambiguous_parent_relationships AS
            SELECT child.aurora_id AS owner_aurora_id,c.link_kind,COUNT(*) AS candidate_count,
                   group_concat(parent.aurora_id,', ') AS candidate_aurora_ids
            FROM parent_relationship_candidates c
            JOIN elements child ON child.element_id=c.owner_element_id
            JOIN elements parent ON parent.element_id=c.parent_element_id
            GROUP BY c.owner_element_id,c.link_kind HAVING COUNT(*)>1;
            """);
    }

    private static void AddSelectorParentMatches(SqliteConnection connection, SqliteTransaction transaction)
    {
        var children = new Dictionary<long, (string Type, AuroraExpressionEvaluationContext Context)>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT e.element_id,et.type_name,e.aurora_id,es.support_text
                FROM temp.parent_owners o
                JOIN elements e ON e.element_id=o.element_id
                JOIN element_types et ON et.element_type_id=e.element_type_id
                LEFT JOIN element_supports es ON es.element_id=e.element_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                if (!children.TryGetValue(id, out var child))
                {
                    child = (reader.GetString(1), new AuroraExpressionEvaluationContext());
                    if (!reader.IsDBNull(2)) child.Context.AddToken(reader.GetString(2));
                    children.Add(id, child);
                }
                if (!reader.IsDBNull(3)) child.Context.AddToken(reader.GetString(3));
            }
        }

        var selectors = new List<(long Id, long Owner, string Type, string Supports, bool HasItems)>();
        var explicitItems = new HashSet<(long Select, long Child)>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT s.select_id,rs.owner_element_id,s.select_type,s.supports_text,
                       EXISTS(SELECT 1 FROM select_items si WHERE si.select_id=s.select_id)
                FROM selects s
                JOIN rule_scopes rs ON rs.rule_scope_id=s.rule_scope_id AND rs.owner_kind='element'
                JOIN resolved_elements_cache rec ON rec.winning_element_id=rs.owner_element_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                selectors.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4)));
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT si.select_id,e.element_id FROM select_items si
                JOIN elements e ON e.aurora_id=si.target_aurora_id
                JOIN temp.parent_owners o ON o.element_id=e.element_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) explicitItems.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO temp.parent_rule_matches VALUES ($child,$parent,$kind);";
        var childParameter = insert.Parameters.Add("$child", SqliteType.Integer);
        var parentParameter = insert.Parameters.Add("$parent", SqliteType.Integer);
        var kindParameter = insert.Parameters.Add("$kind", SqliteType.Text);
        var childrenByType = children.GroupBy(pair => pair.Value.Type).ToArray();
        foreach (var select in selectors)
        {
            if (!childrenByType.Any(group => AuroraSelectionRules.OptionMatchesSelectType(select.Type, group.Key)))
                continue;
            var parsed = string.IsNullOrWhiteSpace(select.Supports) ? null : AuroraExpressionEngine.Parse(select.Supports);
            bool staticSupport = parsed?.Status == "parsed" && AuroraSelectionRules.IsStaticSupportExpression(parsed.RootNode);
            if (parsed == null && !select.HasItems)
                ExecuteInsert(connection, transaction,
                    "INSERT INTO parent_selector_diagnostics VALUES ($id,'deferred','no-static-selection-target',$text);",
                    ("$id", select.Id), ("$text", "No supports or inline IDs declare a static selection target. Supply explicit rule evidence before inferring a parent."));
            if (parsed != null && !staticSupport)
                ExecuteInsert(connection, transaction, """
                    INSERT INTO parent_selector_diagnostics VALUES ($id,$status,$reason,$text);
                    """, ("$id", select.Id), ("$status", parsed.Status == "parsed" ? "deferred" : "actionable"),
                    ("$reason", parsed.Status == "parsed" ? "character-dependent-support" : "invalid-support-expression"),
                    ("$text", parsed.Status == "parsed"
                        ? "Evaluate selector supports with character context; no static parent inferred: " + select.Supports
                        : "Correct selector supports '" + select.Supports + "': " + parsed.ErrorText));

            parentParameter.Value = select.Owner;
            foreach (var group in childrenByType.Where(group => AuroraSelectionRules.OptionMatchesSelectType(select.Type, group.Key)))
            foreach (var child in group)
            {
                if (child.Key == select.Owner) continue;
                bool explicitId = explicitItems.Contains((select.Id, child.Key));
                if (!explicitId && !(staticSupport && AuroraExpressionEngine.Evaluate(parsed.RootNode, child.Value.Context)))
                    continue;
                childParameter.Value = child.Key;
                kindParameter.Value = explicitId ? "select-item-id" : "select-support";
                insert.ExecuteNonQuery();
            }
        }
    }

}
