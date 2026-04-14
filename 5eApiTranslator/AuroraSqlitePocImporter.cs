using _5eApiTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace _5eApiTranslator
{
    internal static class AuroraSqlitePocImporter
    {
        public static void Import(AuroraImportCatalog catalog, string schemaPath, string sqlitePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath) ?? AppContext.BaseDirectory);

            if (File.Exists(sqlitePath))
            {
                File.Delete(sqlitePath);
            }

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath
            }.ToString());

            connection.Open();

            using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = File.ReadAllText(schemaPath);
                schemaCommand.ExecuteNonQuery();
            }

            using var transaction = connection.BeginTransaction();

            Dictionary<string, long> elementTypeIds = LoadElementTypeIds(connection, transaction);
            Dictionary<string, long> sourceBookIds = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, long> sourceFileIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceName in catalog.Elements.Select(x => x.source)
                .Concat(catalog.Spells.Select(x => x.source))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sourceBookIds[sourceName] = InsertSourceBook(connection, transaction, sourceName);
            }

            foreach (var file in catalog.Files)
            {
                sourceFileIds[file.RelativePath] = InsertSourceFile(connection, transaction, file);
            }

            foreach (var element in catalog.Elements)
            {
                if (!elementTypeIds.TryGetValue(element.type, out long elementTypeId))
                {
                    continue;
                }

                long elementId = InsertElementBase(
                    connection,
                    transaction,
                    elementTypeId,
                    sourceBookIds.TryGetValue(element.source ?? string.Empty, out var sourceBookId) ? sourceBookId : (long?)null,
                    sourceFileIds.TryGetValue(element.source_file_path ?? string.Empty, out var sourceFileId) ? sourceFileId : (long?)null,
                    element.id,
                    element.name,
                    element.index,
                    element.compendium.display,
                    DetermineLoaderPriority(element.type));

                InsertElementTexts(connection, transaction, elementId, element);
                InsertElementSupports(connection, transaction, elementId, element.supports);
                InsertElementRequirements(connection, transaction, elementId, element.requirements);

                if (element.spellcasting != null)
                {
                    InsertSpellcastingProfile(connection, transaction, elementId, element.type, element.spellcasting);
                }

                InsertSubtypeRecord(connection, transaction, elementId, element);
                InsertRules(connection, transaction, elementId, "element", element.rules);

                if (string.Equals(element.type, "class", StringComparison.OrdinalIgnoreCase)
                    && element.multiclass != null)
                {
                    InsertClassMulticlass(connection, transaction, elementId, element.multiclass);
                    InsertRules(connection, transaction, elementId, "class-multiclass", element.multiclass.rules);
                }
            }

            foreach (var spell in catalog.Spells)
            {
                if (!elementTypeIds.TryGetValue("Spell", out long elementTypeId))
                {
                    continue;
                }

                long elementId = InsertElementBase(
                    connection,
                    transaction,
                    elementTypeId,
                    sourceBookIds.TryGetValue(spell.source ?? string.Empty, out var sourceBookId) ? sourceBookId : (long?)null,
                    sourceFileIds.TryGetValue(spell.source_file_path ?? string.Empty, out var sourceFileId) ? sourceFileId : (long?)null,
                    spell.aurora_id,
                    spell.name,
                    spell.index,
                    spell.compendium_display,
                    DetermineLoaderPriority("Spell"));

                InsertSpellTexts(connection, transaction, elementId, spell);
                InsertSpellRecord(connection, transaction, elementId, spell);
            }

            ResolveDeferredRelationships(connection, transaction);
            transaction.Commit();
        }

        private static Dictionary<string, long> LoadElementTypeIds(SqliteConnection connection, SqliteTransaction transaction)
        {
            Dictionary<string, long> map = new(StringComparer.OrdinalIgnoreCase);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT element_type_id, type_name FROM element_types;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(1)] = reader.GetInt64(0);
            }

            return map;
        }

        private static long InsertSourceBook(SqliteConnection connection, SqliteTransaction transaction, string sourceName)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO source_books (name) VALUES ($name);";
            command.Parameters.AddWithValue("$name", sourceName);
            command.ExecuteNonQuery();
            return GetLastInsertRowId(connection, transaction);
        }

        private static long InsertSourceFile(SqliteConnection connection, SqliteTransaction transaction, AuroraFileInfo file)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO source_files
(
    relative_path,
    package_name,
    package_description,
    version_text,
    update_file_name,
    update_url,
    author_name,
    author_url
)
VALUES
(
    $relative_path,
    $package_name,
    $package_description,
    $version_text,
    $update_file_name,
    $update_url,
    $author_name,
    $author_url
);";

            command.Parameters.AddWithValue("$relative_path", file.RelativePath);
            command.Parameters.AddWithValue("$package_name", (object)file.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("$package_description", (object)file.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$version_text", (object)file.FileVersion?.versionString ?? DBNull.Value);
            command.Parameters.AddWithValue("$update_file_name", (object)file.FileVersion?.fileName ?? DBNull.Value);
            command.Parameters.AddWithValue("$update_url", (object)file.FileVersion?.fileUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$author_name", (object)file.Author?.name ?? DBNull.Value);
            command.Parameters.AddWithValue("$author_url", (object)file.Author?.url ?? DBNull.Value);
            command.ExecuteNonQuery();

            return GetLastInsertRowId(connection, transaction);
        }

        private static long InsertElementBase(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long elementTypeId,
            long? sourceBookId,
            long? sourceFileId,
            string auroraId,
            string name,
            string slug,
            bool compendiumDisplay,
            int loaderPriority)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO elements
(
    aurora_id,
    element_type_id,
    source_book_id,
    source_file_id,
    name,
    slug,
    compendium_display,
    loader_priority
)
VALUES
(
    $aurora_id,
    $element_type_id,
    $source_book_id,
    $source_file_id,
    $name,
    $slug,
    $compendium_display,
    $loader_priority
);";

            command.Parameters.AddWithValue("$aurora_id", auroraId);
            command.Parameters.AddWithValue("$element_type_id", elementTypeId);
            command.Parameters.AddWithValue("$source_book_id", sourceBookId.HasValue ? sourceBookId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$source_file_id", sourceFileId.HasValue ? sourceFileId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$slug", slug ?? name?.Trim().ToLower().Replace(" ", "-"));
            command.Parameters.AddWithValue("$compendium_display", compendiumDisplay ? 1 : 0);
            command.Parameters.AddWithValue("$loader_priority", loaderPriority);
            command.ExecuteNonQuery();

            return GetLastInsertRowId(connection, transaction);
        }

        private static void InsertElementTexts(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraElement element)
        {
            if (!string.IsNullOrWhiteSpace(element.prerequisite))
            {
                InsertElementText(connection, transaction, elementId, "prerequisite", 1, null, null, null, null, null, element.prerequisite);
            }

            if (!string.IsNullOrWhiteSpace(element.description))
            {
                InsertElementText(connection, transaction, elementId, "description", 1, null, null, null, null, null, element.description);
            }

            if (element.sheet == null)
                return;

            if (element.sheet.description?.Any() == true)
            {
                int ordinal = 1;

                foreach (var sheetDescription in element.sheet.description)
                {
                    InsertElementText(
                        connection,
                        transaction,
                        elementId,
                        "sheet",
                        ordinal++,
                        sheetDescription.level,
                        element.sheet.display,
                        element.sheet.alt,
                        element.sheet.action,
                        element.sheet.usage,
                        sheetDescription.text);
                }
            }
            else
            {
                InsertElementText(
                    connection,
                    transaction,
                    elementId,
                    "sheet",
                    1,
                    null,
                    element.sheet.display,
                    element.sheet.alt,
                    element.sheet.action,
                    element.sheet.usage,
                    string.Empty);
            }
        }

        private static void InsertSpellTexts(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraSpell spell)
        {
            if (spell.desc?.Any() == true)
            {
                InsertElementText(connection, transaction, elementId, "description", 1, null, null, null, null, null, string.Join(Environment.NewLine, spell.desc));
            }

            if (spell.higher_level?.Any() == true)
            {
                InsertElementText(connection, transaction, elementId, "summary", 1, null, null, null, null, null, string.Join(Environment.NewLine, spell.higher_level));
            }
        }

        private static void InsertElementText(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long elementId,
            string textKind,
            int ordinal,
            int? level,
            bool? display,
            string altText,
            string actionText,
            string usageText,
            string body)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO element_texts
(
    element_id,
    text_kind,
    ordinal,
    level,
    display,
    alt_text,
    action_text,
    usage_text,
    body
)
VALUES
(
    $element_id,
    $text_kind,
    $ordinal,
    $level,
    $display,
    $alt_text,
    $action_text,
    $usage_text,
    $body
);";

            command.Parameters.AddWithValue("$element_id", elementId);
            command.Parameters.AddWithValue("$text_kind", textKind);
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$level", level.HasValue ? level.Value : DBNull.Value);
            command.Parameters.AddWithValue("$display", display.HasValue ? (display.Value ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$alt_text", (object)altText ?? DBNull.Value);
            command.Parameters.AddWithValue("$action_text", (object)actionText ?? DBNull.Value);
            command.Parameters.AddWithValue("$usage_text", (object)usageText ?? DBNull.Value);
            command.Parameters.AddWithValue("$body", body ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private static void InsertElementSupports(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraTextCollection supports)
        {
            if (supports == null || supports.Count == 0)
                return;

            int ordinal = 1;

            foreach (var support in supports)
            {
                ExecuteInsert(
                    connection,
                    transaction,
                    "INSERT INTO element_supports (element_id, ordinal, support_text) VALUES ($element_id, $ordinal, $support_text);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$support_text", support));
            }
        }

        private static void InsertElementRequirements(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraTextCollection requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return;

            int ordinal = 1;

            foreach (var requirement in requirements)
            {
                ExecuteInsert(
                    connection,
                    transaction,
                    "INSERT INTO element_requirements (element_id, ordinal, requirement_text) VALUES ($element_id, $ordinal, $requirement_text);",
                    ("$element_id", elementId),
                    ("$ordinal", ordinal++),
                    ("$requirement_text", requirement));
            }
        }

        private static void InsertSubtypeRecord(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraElement element)
        {
            if (string.Equals(element.type, "Source", StringComparison.OrdinalIgnoreCase))
            {
                var authorSetter = element.setters?.FindEntry("author");

                ExecuteInsert(connection, transaction,
                    @"INSERT INTO source_elements
(element_id, abbreviation_text, source_url, image_url, errata_url, author_name, author_abbreviation, author_url, is_official, is_core, is_supplement, is_third_party, release_text)
VALUES
($element_id, $abbreviation_text, $source_url, $image_url, $errata_url, $author_name, $author_abbreviation, $author_url, $is_official, $is_core, $is_supplement, $is_third_party, $release_text);",
                    ("$element_id", elementId),
                    ("$abbreviation_text", (object)element.setters?.GetValue("abbreviation") ?? DBNull.Value),
                    ("$source_url", (object)element.setters?.GetValue("url") ?? DBNull.Value),
                    ("$image_url", (object)element.setters?.GetValue("image") ?? DBNull.Value),
                    ("$errata_url", (object)element.setters?.GetValue("errata") ?? DBNull.Value),
                    ("$author_name", (object)authorSetter?.value ?? DBNull.Value),
                    ("$author_abbreviation", (object)authorSetter?.GetAttribute("abbreviation") ?? DBNull.Value),
                    ("$author_url", (object)authorSetter?.GetAttribute("url") ?? DBNull.Value),
                    ("$is_official", element.setters?.GetBoolean("official").HasValue == true ? (element.setters.GetBoolean("official").Value ? 1 : 0) : DBNull.Value),
                    ("$is_core", element.setters?.GetBoolean("core").HasValue == true ? (element.setters.GetBoolean("core").Value ? 1 : 0) : DBNull.Value),
                    ("$is_supplement", element.setters?.GetBoolean("supplement").HasValue == true ? (element.setters.GetBoolean("supplement").Value ? 1 : 0) : DBNull.Value),
                    ("$is_third_party", element.setters?.GetBoolean("third-party").HasValue == true ? (element.setters.GetBoolean("third-party").Value ? 1 : 0) : DBNull.Value),
                    ("$release_text", (object)element.setters?.GetValue("release") ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Class", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO classes (element_id, hit_die, short_text) VALUES ($element_id, $hit_die, $short_text);",
                    ("$element_id", elementId),
                    ("$hit_die", (object)element.setters?.hd ?? DBNull.Value),
                    ("$short_text", (object)element.setters?.@short ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Archetype", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO archetypes (element_id, parent_support_text) VALUES ($element_id, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Race", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO races (element_id, names_format_text) VALUES ($element_id, $names_format_text);",
                    ("$element_id", elementId),
                    ("$names_format_text", (object)element.setters?.GetValue("names-format") ?? DBNull.Value));

                int ordinal = 1;
                foreach (var nameGroup in element.setters?.names ?? Enumerable.Empty<Names>())
                {
                    foreach (var nameValue in nameGroup.names ?? Enumerable.Empty<string>())
                    {
                        ExecuteInsert(connection, transaction,
                            "INSERT INTO race_name_groups (race_element_id, ordinal, name_group_type, name_value) VALUES ($race_element_id, $ordinal, $name_group_type, $name_value);",
                            ("$race_element_id", elementId),
                            ("$ordinal", ordinal++),
                            ("$name_group_type", (object)nameGroup.type ?? DBNull.Value),
                            ("$name_value", nameValue));
                    }
                }
                return;
            }

            if (string.Equals(element.type, "Sub Race", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO subraces (element_id, parent_support_text) VALUES ($element_id, $parent_support_text);",
                    ("$element_id", elementId),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value));
                return;
            }

            if (string.Equals(element.type, "Background", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction, "INSERT INTO backgrounds (element_id) VALUES ($element_id);", ("$element_id", elementId));
                return;
            }

            if (string.Equals(element.type, "Feat", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO feats (element_id, allow_duplicate) VALUES ($element_id, $allow_duplicate);",
                    ("$element_id", elementId),
                    ("$allow_duplicate", element.setters?.GetBoolean("allow duplicate") == true ? 1 : 0));
                return;
            }

            if (string.Equals(element.type, "Language", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO languages (element_id, script_text, speakers_text, is_standard, is_exotic, is_secret)
VALUES ($element_id, $script_text, $speakers_text, $is_standard, $is_exotic, $is_secret);",
                    ("$element_id", elementId),
                    ("$script_text", (object)element.setters?.script ?? DBNull.Value),
                    ("$speakers_text", (object)element.setters?.speakers ?? DBNull.Value),
                    ("$is_standard", element.setters?.standard == true ? 1 : 0),
                    ("$is_exotic", element.setters?.exotic == true ? 1 : 0),
                    ("$is_secret", element.setters?.secret == true ? 1 : 0));
                return;
            }

            if (string.Equals(element.type, "Proficiency", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInsert(connection, transaction,
                    "INSERT INTO proficiencies (element_id, proficiency_group, proficiency_subgroup) VALUES ($element_id, $proficiency_group, $proficiency_subgroup);",
                    ("$element_id", elementId),
                    ("$proficiency_group", (object)element.supports?.FirstOrDefault() ?? DBNull.Value),
                    ("$proficiency_subgroup", element.supports?.Count > 1 ? element.supports[1] : DBNull.Value));
                return;
            }

            if (IsFeatureType(element.type))
            {
                var minimumLevel = GetMinimumLevel(element);

                ExecuteInsert(connection, transaction,
                    "INSERT INTO features (element_id, feature_kind, parent_support_text, min_level) VALUES ($element_id, $feature_kind, $parent_support_text, $min_level);",
                    ("$element_id", elementId),
                    ("$feature_kind", element.type),
                    ("$parent_support_text", (object)element.supports?.FirstOrDefault() ?? DBNull.Value),
                    ("$min_level", minimumLevel.HasValue ? minimumLevel.Value : DBNull.Value));
                return;
            }

            if (IsItemType(element.type))
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO items
(element_id, item_kind, cost_text, weight_text, damage_dice_text, damage_type_text, armor_class_text, properties_text, speed_text, capacity_text)
VALUES
($element_id, $item_kind, $cost_text, $weight_text, $damage_dice_text, $damage_type_text, $armor_class_text, $properties_text, $speed_text, $capacity_text);",
                    ("$element_id", elementId),
                    ("$item_kind", element.type),
                    ("$cost_text", (object)element.setters?.GetValue("cost") ?? DBNull.Value),
                    ("$weight_text", (object)element.setters?.GetValue("weight") ?? DBNull.Value),
                    ("$damage_dice_text", (object)element.setters?.GetValue("damage") ?? DBNull.Value),
                    ("$damage_type_text", (object)element.setters?.GetValue("damage type") ?? DBNull.Value),
                    ("$armor_class_text", (object)element.setters?.GetValue("armor class") ?? DBNull.Value),
                    ("$properties_text", (object)element.supports?.raw ?? DBNull.Value),
                    ("$speed_text", (object)element.setters?.GetValue("speed") ?? DBNull.Value),
                    ("$capacity_text", (object)element.setters?.GetValue("capacity") ?? DBNull.Value));
            }
        }

        private static void InsertClassMulticlass(SqliteConnection connection, SqliteTransaction transaction, long elementId, Multiclass multiclass)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO class_multiclass
(class_element_id, multiclass_aurora_id, prerequisite_text, requirements_text, proficiencies_text)
VALUES
($class_element_id, $multiclass_aurora_id, $prerequisite_text, $requirements_text, $proficiencies_text);",
                ("$class_element_id", elementId),
                ("$multiclass_aurora_id", (object)multiclass.id ?? DBNull.Value),
                ("$prerequisite_text", (object)multiclass.prerequisite ?? DBNull.Value),
                ("$requirements_text", (object)multiclass.requirements?.raw ?? DBNull.Value),
                ("$proficiencies_text", (object)multiclass.setters?.GetValue("multiclass proficiencies") ?? DBNull.Value));
        }

        private static void InsertSpellcastingProfile(SqliteConnection connection, SqliteTransaction transaction, long elementId, string elementType, Spellcasting spellcasting)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO spellcasting_profiles
(owner_element_id, owner_kind, profile_name, ability_name, is_extended, prepare_spells, allow_replace, list_text, extend_text)
VALUES
($owner_element_id, $owner_kind, $profile_name, $ability_name, $is_extended, $prepare_spells, $allow_replace, $list_text, $extend_text);",
                ("$owner_element_id", elementId),
                ("$owner_kind", GetSpellcastingOwnerKind(elementType)),
                ("$profile_name", spellcasting.name ?? "Spellcasting"),
                ("$ability_name", (object)spellcasting.ability ?? DBNull.Value),
                ("$is_extended", spellcasting.extend ? 1 : 0),
                ("$prepare_spells", spellcasting.prepare.HasValue ? (spellcasting.prepare.Value ? 1 : 0) : DBNull.Value),
                ("$allow_replace", spellcasting.allowReplace.HasValue ? (spellcasting.allowReplace.Value ? 1 : 0) : DBNull.Value),
                ("$list_text", (object)spellcasting.list?.raw ?? DBNull.Value),
                ("$extend_text", (object)spellcasting.extendList?.raw ?? DBNull.Value));
        }

        private static void InsertRules(SqliteConnection connection, SqliteTransaction transaction, long elementId, string ownerKind, Rules rules)
        {
            if (rules == null)
                return;

            if (!(rules.grants?.Any() == true || rules.selects?.Any() == true || rules.stats?.Any() == true))
                return;

            long ruleScopeId = InsertRuleScope(connection, transaction, ownerKind, elementId);

            int ordinal = 1;
            foreach (var grant in rules.grants ?? Enumerable.Empty<Grant>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO grants
(rule_scope_id, ordinal, grant_type, target_aurora_id, name_text, grant_level, requirements_text)
VALUES
($rule_scope_id, $ordinal, $grant_type, $target_aurora_id, $name_text, $grant_level, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$grant_type", grant.type ?? string.Empty),
                    ("$target_aurora_id", (object)grant.id ?? DBNull.Value),
                    ("$name_text", (object)grant.name ?? DBNull.Value),
                    ("$grant_level", grant.level.HasValue ? grant.level.Value : DBNull.Value),
                    ("$requirements_text", (object)grant.requirements?.raw ?? DBNull.Value));
            }

            ordinal = 1;
            foreach (var select in rules.selects ?? Enumerable.Empty<Select>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO selects
(rule_scope_id, ordinal, select_type, name_text, supports_text, select_level, number_to_choose, default_choice_text, is_optional, requirements_text)
VALUES
($rule_scope_id, $ordinal, $select_type, $name_text, $supports_text, $select_level, $number_to_choose, $default_choice_text, $is_optional, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$select_type", select.type ?? string.Empty),
                    ("$name_text", select.name ?? string.Empty),
                    ("$supports_text", (object)select.supports?.raw ?? DBNull.Value),
                    ("$select_level", select.level.HasValue ? select.level.Value : DBNull.Value),
                    ("$number_to_choose", select.number),
                    ("$default_choice_text", (object)select.defaultChoice ?? DBNull.Value),
                    ("$is_optional", select.optional ? 1 : 0),
                    ("$requirements_text", (object)select.requirements?.raw ?? DBNull.Value));

                long selectId = GetLastInsertRowId(connection, transaction);
                int supportOrdinal = 1;
                foreach (var support in select.supports ?? Enumerable.Empty<string>())
                {
                    ExecuteInsert(connection, transaction,
                        "INSERT INTO select_supports (select_id, ordinal, support_text) VALUES ($select_id, $ordinal, $support_text);",
                        ("$select_id", selectId),
                        ("$ordinal", supportOrdinal++),
                        ("$support_text", support));
                }
            }

            ordinal = 1;
            foreach (var stat in rules.stats ?? Enumerable.Empty<Stat>())
            {
                ExecuteInsert(connection, transaction,
                    @"INSERT INTO stats
(rule_scope_id, ordinal, stat_name, value_expression_text, bonus_expression_text, equipped_expression_text, stat_level, inline_display, alt_text, requirements_text)
VALUES
($rule_scope_id, $ordinal, $stat_name, $value_expression_text, $bonus_expression_text, $equipped_expression_text, $stat_level, $inline_display, $alt_text, $requirements_text);",
                    ("$rule_scope_id", ruleScopeId),
                    ("$ordinal", ordinal++),
                    ("$stat_name", stat.name ?? string.Empty),
                    ("$value_expression_text", (object)stat.value ?? DBNull.Value),
                    ("$bonus_expression_text", (object)stat.bonus ?? DBNull.Value),
                    ("$equipped_expression_text", (object)stat.equipped?.raw ?? DBNull.Value),
                    ("$stat_level", stat.level.HasValue ? stat.level.Value : DBNull.Value),
                    ("$inline_display", stat.inline ? 1 : 0),
                    ("$alt_text", (object)stat.alt ?? DBNull.Value),
                    ("$requirements_text", (object)stat.requirements?.raw ?? DBNull.Value));
            }
        }

        private static long InsertRuleScope(SqliteConnection connection, SqliteTransaction transaction, string ownerKind, long ownerElementId)
        {
            ExecuteInsert(connection, transaction,
                "INSERT INTO rule_scopes (owner_kind, owner_element_id, scope_key) VALUES ($owner_kind, $owner_element_id, $scope_key);",
                ("$owner_kind", ownerKind),
                ("$owner_element_id", ownerElementId),
                ("$scope_key", ownerKind == "class-multiclass" ? "multiclass" : "element"));
            return GetLastInsertRowId(connection, transaction);
        }

        private static void InsertSpellRecord(SqliteConnection connection, SqliteTransaction transaction, long elementId, AuroraSpell spell)
        {
            ExecuteInsert(connection, transaction,
                @"INSERT INTO spells
(element_id, spell_level, school_name, casting_time_text, range_text, duration_text, has_verbal, has_somatic, has_material, material_text, is_concentration, is_ritual, attack_type, damage_type_text, damage_formula_text, dc_ability_name, dc_success_text, source_url)
VALUES
($element_id, $spell_level, $school_name, $casting_time_text, $range_text, $duration_text, $has_verbal, $has_somatic, $has_material, $material_text, $is_concentration, $is_ritual, $attack_type, $damage_type_text, $damage_formula_text, $dc_ability_name, $dc_success_text, $source_url);",
                ("$element_id", elementId),
                ("$spell_level", spell.level),
                ("$school_name", (object)spell.school?.index ?? DBNull.Value),
                ("$casting_time_text", (object)spell.casting_time ?? DBNull.Value),
                ("$range_text", (object)spell.range ?? DBNull.Value),
                ("$duration_text", (object)spell.duration ?? DBNull.Value),
                ("$has_verbal", spell.hasVerbal ? 1 : 0),
                ("$has_somatic", spell.hasSomatic ? 1 : 0),
                ("$has_material", spell.hasMaterial ? 1 : 0),
                ("$material_text", (object)spell.material ?? DBNull.Value),
                ("$is_concentration", spell.concentration ? 1 : 0),
                ("$is_ritual", spell.ritual ? 1 : 0),
                ("$attack_type", (object)spell.attack_type ?? DBNull.Value),
                ("$damage_type_text", (object)spell.damage?.damage_type?.index ?? DBNull.Value),
                ("$damage_formula_text", JsonSerializer.Serialize(spell.damage?.damage_at_slot_level, new JsonSerializerOptions { IncludeFields = true })),
                ("$dc_ability_name", (object)spell.dc?.index ?? DBNull.Value),
                ("$dc_success_text", (object)spell.dc?.dc_success ?? DBNull.Value),
                ("$source_url", (object)spell.url ?? DBNull.Value));

            if (spell.classes?.Any() == true)
            {
                int ordinal = 1;
                foreach (var access in spell.classes)
                {
                    ExecuteInsert(connection, transaction,
                        "INSERT INTO spell_access (spell_element_id, ordinal, access_text) VALUES ($spell_element_id, $ordinal, $access_text);",
                        ("$spell_element_id", elementId),
                        ("$ordinal", ordinal++),
                        ("$access_text", access.name));
                }
            }
        }

        private static void ResolveDeferredRelationships(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteSql(connection, transaction, @"
UPDATE grants
SET target_element_id =
(
    SELECT MIN(e.element_id)
    FROM elements AS e
    WHERE e.aurora_id = grants.target_aurora_id
)
WHERE target_element_id IS NULL
  AND target_aurora_id IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE subraces
SET race_element_id =
(
    SELECT MIN(parent.element_id)
    FROM races AS r
    JOIN elements AS parent ON parent.element_id = r.element_id
    WHERE parent.aurora_id = subraces.parent_support_text
       OR parent.name = subraces.parent_support_text
       OR subraces.parent_support_text = parent.name || ' Subrace'
       OR subraces.parent_support_text = parent.name || ' Ancestry'
       OR subraces.parent_support_text LIKE '% ' || parent.name
)
WHERE race_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE features
SET parent_element_id =
(
    SELECT MIN(parent.element_id)
    FROM elements AS parent
    WHERE parent.aurora_id = features.parent_support_text
       OR parent.name = features.parent_support_text
)
WHERE parent_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS class_element
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    WHERE et.type_name = 'Class'
      AND
      (
          class_element.name = archetypes.parent_support_text
          OR archetypes.parent_support_text = class_element.name || ' Subclass'
          OR (archetypes.parent_support_text = 'Sacred Oath' AND class_element.name = 'Paladin')
          OR (archetypes.parent_support_text = 'Divine Domain' AND class_element.name = 'Cleric')
          OR (archetypes.parent_support_text = 'Bard College' AND class_element.name = 'Bard')
          OR (archetypes.parent_support_text = 'Druid Circle' AND class_element.name = 'Druid')
          OR (archetypes.parent_support_text = 'Martial Archetype' AND class_element.name = 'Fighter')
          OR (archetypes.parent_support_text = 'Monastic Tradition' AND class_element.name = 'Monk')
          OR (archetypes.parent_support_text = 'Ranger Archetype' AND class_element.name = 'Ranger')
          OR (archetypes.parent_support_text = 'Ranger Conclave' AND class_element.name = 'Ranger')
          OR (archetypes.parent_support_text = 'Roguish Archetype' AND class_element.name = 'Rogue')
          OR (archetypes.parent_support_text = 'Sorcerous Origin' AND class_element.name = 'Sorcerer')
          OR (archetypes.parent_support_text = 'Arcane Tradition' AND class_element.name = 'Wizard')
          OR (archetypes.parent_support_text = 'Otherworldly Patron' AND class_element.name = 'Warlock')
          OR (archetypes.parent_support_text = 'Primal Path' AND class_element.name = 'Barbarian')
      )
)
WHERE parent_class_element_id IS NULL
  AND parent_support_text IS NOT NULL;");

            ExecuteSql(connection, transaction, @"
UPDATE archetypes
SET parent_class_element_id =
(
    SELECT MIN(class_element.element_id)
    FROM elements AS archetype_element
    JOIN elements AS class_element ON class_element.source_file_id = archetype_element.source_file_id
    JOIN element_types AS et ON et.element_type_id = class_element.element_type_id
    WHERE archetype_element.element_id = archetypes.element_id
      AND et.type_name = 'Class'
)
WHERE parent_class_element_id IS NULL;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO support_tags (support_text, normalized_text)
SELECT support_text, lower(trim(support_text))
FROM
(
    SELECT support_text FROM element_supports
    UNION
    SELECT support_text FROM select_supports
);");

            ExecuteSql(connection, transaction, @"
INSERT INTO element_support_links
(
    element_id,
    ordinal,
    support_tag_id,
    linked_element_id,
    resolution_kind,
    is_primary_parent
)
SELECT
    es.element_id,
    es.ordinal,
    st.support_tag_id,
    COALESCE(
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.aurora_id = es.support_text),
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.name = es.support_text)
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.aurora_id = es.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.name = es.support_text) THEN 'element-name'
        WHEN es.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind,
    0 AS is_primary_parent
FROM element_supports AS es
JOIN support_tags AS st
    ON st.support_text = es.support_text;");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT a.parent_class_element_id
        FROM archetypes AS a
        WHERE a.element_id = element_support_links.element_id
    ),
    resolution_kind = 'archetype-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM archetypes AS a
      WHERE a.element_id = element_support_links.element_id
        AND a.parent_class_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT s.race_element_id
        FROM subraces AS s
        WHERE s.element_id = element_support_links.element_id
    ),
    resolution_kind = 'subrace-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM subraces AS s
      WHERE s.element_id = element_support_links.element_id
        AND s.race_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
UPDATE element_support_links
SET linked_element_id = (
        SELECT f.parent_element_id
        FROM features AS f
        WHERE f.element_id = element_support_links.element_id
    ),
    resolution_kind = 'feature-parent',
    is_primary_parent = 1
WHERE ordinal = 1
  AND EXISTS
  (
      SELECT 1
      FROM features AS f
      WHERE f.element_id = element_support_links.element_id
        AND f.parent_element_id IS NOT NULL
  );");

            ExecuteSql(connection, transaction, @"
INSERT INTO select_support_links
(
    select_id,
    ordinal,
    support_tag_id,
    linked_element_id,
    resolution_kind
)
SELECT
    ss.select_id,
    ss.ordinal,
    st.support_tag_id,
    COALESCE(
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.aurora_id = ss.support_text),
        (SELECT MIN(e.element_id) FROM elements AS e WHERE e.name = ss.support_text)
    ) AS linked_element_id,
    CASE
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.aurora_id = ss.support_text) THEN 'aurora-id'
        WHEN EXISTS(SELECT 1 FROM elements AS e WHERE e.name = ss.support_text) THEN 'element-name'
        WHEN ss.support_text LIKE '$(%' THEN 'dynamic'
        ELSE 'support-category'
    END AS resolution_kind
FROM select_supports AS ss
JOIN support_tags AS st
    ON st.support_text = ss.support_text;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    es.element_id,
    ssl.support_tag_id,
    'support-membership'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN element_supports AS esupport
    ON esupport.support_text = st.support_text
JOIN elements AS es
    ON es.element_id = esupport.element_id;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    e.element_id,
    ssl.support_tag_id,
    'direct-id'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN elements AS e
    ON e.aurora_id = st.support_text;");

            ExecuteSql(connection, transaction, @"
INSERT OR IGNORE INTO select_option_links
(
    select_id,
    option_element_id,
    support_tag_id,
    match_kind
)
SELECT
    ssl.select_id,
    e.element_id,
    ssl.support_tag_id,
    'direct-name'
FROM select_support_links AS ssl
JOIN support_tags AS st
    ON st.support_tag_id = ssl.support_tag_id
JOIN elements AS e
    ON e.name = st.support_text;");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'dynamic-expression'
WHERE support_text LIKE '$(%';");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'dynamic-expression'
WHERE support_kind = 'unclassified'
  AND
  (
      support_text LIKE '%||%'
      OR support_text LIKE '%&&%'
      OR support_text LIKE '!(%'
      OR support_text LIKE '!%'
      OR support_text LIKE '%,%'
      OR support_text LIKE '(%'
      OR support_text GLOB '[0-9]'
      OR support_text GLOB '[0-9][0-9]'
      OR support_text GLOB 'ID_*|*'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'direct-parent'
WHERE EXISTS
(
    SELECT 1
    FROM element_support_links AS esl
    WHERE esl.support_tag_id = support_tags.support_tag_id
      AND esl.is_primary_parent = 1
);");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'broad-option-set'
WHERE support_kind = 'unclassified'
  AND normalized_text IN
  (
      'skill',
      'tool',
      'language',
      'weapon',
      'armor',
      'item',
      'magic item',
      'mount',
      'vehicle',
      'ammunition',
      'general',
      'melee',
      'ranged',
      'simple',
      'martial',
      'musical instrument',
      'artisan''s tools',
      'artisan tools',
      'gaming set',
      'vehicle (land)',
      'vehicle (water)',
      'class',
      'race',
      'spell attack'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND normalized_text IN
  (
      'abjuration',
      'conjuration',
      'divination',
      'enchantment',
      'evocation',
      'illusion',
      'necromancy',
      'transmutation',
      'ritual',
      'companion',
      'familiar',
      'background variant',
      'custom race language',
      'psionic disciplines',
      'sub-feature'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND
  (
      normalized_text = 'starting'
      OR normalized_text LIKE '% discipline'
      OR normalized_text LIKE '% specialization'
      OR normalized_text LIKE 'variant %'
      OR normalized_text LIKE '% variant'
      OR normalized_text LIKE '% companion'
      OR normalized_text LIKE '% companions'
      OR normalized_text LIKE '% spirit'
      OR normalized_text LIKE 'spirit bonded %'
      OR normalized_text LIKE 'undead servant %'
      OR normalized_text LIKE 'ua artificer %'
      OR normalized_text LIKE 'ua2020% %'
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM select_option_links AS sol
      WHERE sol.support_tag_id = support_tags.support_tag_id
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM element_support_links AS esl
      WHERE esl.support_tag_id = support_tags.support_tag_id
  );");

            ExecuteSql(connection, transaction, @"
UPDATE support_tags
SET support_kind = 'bounded-option-set'
WHERE support_kind = 'unclassified'
  AND EXISTS
  (
      SELECT 1
      FROM select_support_links AS ssl
      WHERE ssl.support_tag_id = support_tags.support_tag_id
  );");
        }

        private static void ExecuteSql(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static long GetLastInsertRowId(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT last_insert_rowid();";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        private static void ExecuteInsert(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        private static string GetSpellcastingOwnerKind(string elementType)
        {
            if (string.Equals(elementType, "Class", StringComparison.OrdinalIgnoreCase))
                return "class";

            if (string.Equals(elementType, "Archetype", StringComparison.OrdinalIgnoreCase))
                return "archetype";

            return "feature";
        }

        private static int DetermineLoaderPriority(string elementType)
        {
            return elementType?.ToLowerInvariant() switch
            {
                "source" => 5,
                "race" => 10,
                "sub race" => 20,
                "class" => 30,
                "archetype" => 40,
                "background" => 50,
                "feat" => 60,
                "language" => 70,
                "proficiency" => 80,
                "spell" => 90,
                "class feature" => 100,
                "archetype feature" => 110,
                "racial trait" => 120,
                "background feature" => 130,
                "feat feature" => 140,
                "ability score improvement" => 150,
                _ => 500
            };
        }

        private static int? GetMinimumLevel(AuroraElement element)
        {
            List<int> levels = new();

            if (element.sheet?.description?.Any() == true)
            {
                levels.AddRange(element.sheet.description.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.grants?.Any() == true)
            {
                levels.AddRange(element.rules.grants.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.selects?.Any() == true)
            {
                levels.AddRange(element.rules.selects.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            if (element.rules?.stats?.Any() == true)
            {
                levels.AddRange(element.rules.stats.Where(x => x.level.HasValue).Select(x => x.level.Value));
            }

            return levels.Count > 0 ? levels.Min() : null;
        }

        private static bool IsFeatureType(string elementType)
        {
            return string.Equals(elementType, "Class Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Archetype Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Racial Trait", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Background Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Feat Feature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Ability Score Improvement", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsItemType(string elementType)
        {
            return string.Equals(elementType, "Item", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Weapon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Armor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Ammunition", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Mount", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Vehicle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(elementType, "Magic Item", StringComparison.OrdinalIgnoreCase);
        }
    }
}

