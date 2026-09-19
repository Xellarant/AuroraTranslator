using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

internal static partial class AuroraSqliteImporter
{
    private static void RebuildStaticSelectionOptions(SqliteConnection connection, SqliteTransaction transaction)
    {
        var selectors = new List<(int Id, string Type, string Supports)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT select_id,select_type,supports_text FROM selects WHERE trim(COALESCE(supports_text,''))<>'';";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                if (AuroraSelectionRules.UsesGenericElementPool(reader.GetString(1)))
                    selectors.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        var options = AuroraSelectionRules.LoadSupportOptions(connection, transaction).Values.GroupBy(o => o.Type).ToArray();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO select_option_links VALUES ($select,$option,$tag,'support-expression');";
        var selectParameter = insert.Parameters.Add("$select", SqliteType.Integer);
        var optionParameter = insert.Parameters.Add("$option", SqliteType.Integer);
        var tagParameter = insert.Parameters.Add("$tag", SqliteType.Integer);
        foreach (var select in selectors)
        {
            var expression = AuroraExpressionEngine.Parse(select.Supports);
            if (expression.Status == "parsed" && !AuroraSelectionRules.IsStaticSupportExpression(expression.RootNode)) continue;
            // Candidate caches must honor the whole expression, including exclusions.
            // Preserve independently authored inline choices.
            ExecuteInsert(connection, transaction, "DELETE FROM select_option_links WHERE select_id=$id AND match_kind NOT IN ('inline-item-id','inline-item-text');", ("$id", select.Id));
            if (expression.Status != "parsed") continue;
            ExecuteInsert(connection, transaction, "INSERT OR IGNORE INTO support_tags (support_text,normalized_text,support_kind) VALUES ($text,$normalized,'bounded-option-set');",
                ("$text", select.Supports), ("$normalized", select.Supports.Trim().ToLowerInvariant()));
            using var tag = connection.CreateCommand();
            tag.Transaction = transaction;
            tag.CommandText = "SELECT support_tag_id FROM support_tags WHERE support_text=$text;";
            tag.Parameters.AddWithValue("$text", select.Supports);
            tagParameter.Value = tag.ExecuteScalar();
            selectParameter.Value = select.Id;
            foreach (var group in options.Where(g => AuroraSelectionRules.OptionMatchesSelectType(select.Type, g.Key)))
            foreach (var option in group)
            {
                if (!AuroraSelectionRules.Matches(expression, option)) continue;
                optionParameter.Value = option.Id;
                insert.ExecuteNonQuery();
            }
        }
    }
}
