using _5eApiTranslator.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace _5eApiTranslator
{
    /// <summary>
    /// Generates an Aurora Builder-compatible XML file for "The Book of Xellarant"
    /// containing SRD creatures that do not already have an Aurora Companion element.
    /// </summary>
    internal static class XellarantXmlGenerator
    {
        private const string SourceName = "The Book of Xellarant";
        private const string SourceId   = "ID_XELLARANT_SOURCE_BOOK_OF_XELLARANT";
        private const string IdPrefix   = "ID_XELLARANT";
        private const string AuthorUrl  = "https://github.com/Xellarant/the-book-of-xellarant";

        public static void Generate(string jsonPath, string sqlitePath, string outputPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"SRD monsters JSON not found: {jsonPath}");
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database not found: {sqlitePath}");

            // ── 1. Load SRD JSON ─────────────────────────────────────────────────
            var allMonsters = JsonSerializer.Deserialize<List<SrdMonster>>(
                File.ReadAllText(jsonPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (allMonsters == null || allMonsters.Count == 0)
                throw new InvalidOperationException("No monsters found in JSON.");

            // ── 2. Find which SRD creature slugs are already linked to Aurora ────
            var alreadyLinked = LoadLinkedSlugs(sqlitePath);

            // ── 3. Filter to creatures not yet covered by Aurora ─────────────────
            var toGenerate = allMonsters
                .Where(m => !alreadyLinked.Contains(m.Index ?? Slugify(m.Name)))
                .OrderBy(m => m.ChallengeRating)
                .ThenBy(m => m.Type)
                .ThenBy(m => m.Name)
                .ToList();

            Console.WriteLine($"Generating XML for {toGenerate.Count} creatures " +
                              $"({allMonsters.Count - toGenerate.Count} already covered by Aurora).");

            // ── 4. Write XML ─────────────────────────────────────────────────────
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

            var settings = new XmlWriterSettings
            {
                Indent      = true,
                IndentChars = "\t",
                Encoding    = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                NewLineChars = "\n"
            };

            using var writer = XmlWriter.Create(outputPath, settings);

            writer.WriteStartDocument();
            writer.WriteStartElement("elements");
            writer.WriteComment(" The Book of Xellarant — creatures compiled from the SRD 5.1 (CC BY 4.0) ");

            WriteInfoBlock(writer);
            WriteSourceElement(writer, toGenerate.Count);

            foreach (var monster in toGenerate)
                WriteMonster(writer, monster);

            writer.WriteEndElement(); // </elements>
            writer.WriteEndDocument();

            Console.WriteLine($"Written to {outputPath}");
        }

        // ── XML sections ─────────────────────────────────────────────────────────

        private static void WriteInfoBlock(XmlWriter w)
        {
            w.WriteStartElement("info");

            WriteElement(w, "name", "The Book of Xellarant");
            WriteElement(w, "description",
                "Creatures compiled from the D&D 5e System Reference Document 5.1 (CC BY 4.0). " +
                "Includes SRD beasts, dragons, monstrosities, and other creatures not already " +
                "present in the standard Aurora Builder content files.");
            w.WriteStartElement("author");
            w.WriteAttributeString("url", AuthorUrl);
            w.WriteString("Xellarant");
            w.WriteEndElement();

            w.WriteStartElement("update");
            w.WriteAttributeString("version", "0.0.1");
            w.WriteStartElement("file");
            w.WriteAttributeString("name", "creatures.xml");
            w.WriteAttributeString("url",
                "https://raw.githubusercontent.com/Xellarant/the-book-of-xellarant/main/creatures.xml");
            w.WriteEndElement(); // </file>
            w.WriteEndElement(); // </update>

            w.WriteEndElement(); // </info>
        }

        private static void WriteSourceElement(XmlWriter w, int creatureCount)
        {
            w.WriteStartElement("element");
            w.WriteAttributeString("name",   "The Book of Xellarant");
            w.WriteAttributeString("type",   "Source");
            w.WriteAttributeString("source", SourceName);
            w.WriteAttributeString("id",     SourceId);

            w.WriteStartElement("description");
            w.WriteStartElement("p");
            w.WriteString(
                $"The Book of Xellarant is a custom compilation of {creatureCount} creatures " +
                "sourced from the D&D 5e System Reference Document 5.1 (CC BY 4.0). " +
                "It covers beasts, dragons, monstrosities, undead, and other creature types " +
                "not included in the standard Aurora Builder content files.");
            w.WriteEndElement(); // </p>
            w.WriteEndElement(); // </description>

            w.WriteStartElement("setters");
            WriteSet(w, "abbreviation",  "TBOX");
            WriteSet(w, "url",           AuthorUrl);
            WriteSet(w, "official",      "false");
            WriteSet(w, "supplement",    "false");
            WriteSet(w, "third-party",   "false");
            w.WriteEndElement(); // </setters>

            w.WriteEndElement(); // </element>
        }

        private static void WriteMonster(XmlWriter w, SrdMonster m)
        {
            string monsterSlug = m.Index ?? Slugify(m.Name);
            string companionId = $"{IdPrefix}_COMPANION_{ToIdPart(monsterSlug)}";

            // Collect trait and action IDs before writing the companion element
            var traitIds  = BuildChildIds(m.SpecialAbilities, monsterSlug, "TRAIT");
            var actionIds = BuildChildIds(m.Actions,          monsterSlug, "ACTION");

            w.WriteComment($" {m.Name} (CR {SrdHelpers.FormatCr(m.ChallengeRating)}, {SrdHelpers.Capitalize(m.Type)}) ");

            // ── Companion element ─────────────────────────────────────────────
            w.WriteStartElement("element");
            w.WriteAttributeString("name",   m.Name);
            w.WriteAttributeString("type",   "Companion");
            w.WriteAttributeString("source", SourceName);
            w.WriteAttributeString("id",     companionId);

            // Supports for familiars: Tiny beasts/fey/fiends with fly or no swim
            if (IsFamiliarCandidate(m))
                WriteElement(w, "supports", "Familiar");

            WriteDescriptionElement(w, m);

            // Setters
            w.WriteStartElement("setters");
            WriteSet(w, "size",          SrdHelpers.Capitalize(m.Size ?? "Medium"));
            WriteSet(w, "type",          SrdHelpers.Capitalize(m.Type ?? "Beast"));
            WriteSet(w, "alignment",     m.Alignment ?? "unaligned");
            WriteSet(w, "ac",            SrdHelpers.FormatAc(m.ArmorClass, "10"));
            WriteSet(w, "hp",            SrdHelpers.FormatHp(m.HitPoints, m.HitPointsRoll));
            WriteSet(w, "speed",         SrdHelpers.FormatSpeed(m.Speed, "30 ft."));
            WriteSet(w, "strength",      m.Strength.ToString());
            WriteSet(w, "dexterity",     m.Dexterity.ToString());
            WriteSet(w, "constitution",  m.Constitution.ToString());
            WriteSet(w, "intelligence",  m.Intelligence.ToString());
            WriteSet(w, "wisdom",        m.Wisdom.ToString());
            WriteSet(w, "charisma",      m.Charisma.ToString());

            var skills = SrdHelpers.FormatSkills(m.Proficiencies);
            if (!string.IsNullOrEmpty(skills))
                WriteSet(w, "skills", skills);

            var saves = SrdHelpers.FormatSavingThrows(m.Proficiencies);
            if (!string.IsNullOrEmpty(saves))
                WriteSet(w, "saves", saves);

            var senses = SrdHelpers.FormatSenses(m.Senses);
            if (!string.IsNullOrEmpty(senses))
                WriteSet(w, "senses", senses);

            if (m.DamageResistances?.Count > 0)
                WriteSet(w, "resistances", string.Join(", ", m.DamageResistances));
            if (m.DamageImmunities?.Count > 0)
                WriteSet(w, "immunities", string.Join(", ", m.DamageImmunities));
            if (m.DamageVulnerabilities?.Count > 0)
                WriteSet(w, "vulnerabilities", string.Join(", ", m.DamageVulnerabilities));
            if (m.ConditionImmunities?.Count > 0)
                WriteSet(w, "condition-immunities", string.Join(", ", m.ConditionImmunities.Select(ci => ci.Name)));

            WriteSet(w, "languages",  string.IsNullOrWhiteSpace(m.Languages) ? "\u2014" : m.Languages);
            WriteSet(w, "challenge",  SrdHelpers.FormatCr(m.ChallengeRating));
            WriteSet(w, "proficiency", m.ProficiencyBonus.ToString());

            if (traitIds.Count > 0)
                WriteSet(w, "traits",  string.Join(",", traitIds));
            if (actionIds.Count > 0)
                WriteSet(w, "actions", string.Join(",", actionIds));

            w.WriteEndElement(); // </setters>

            // Rules
            w.WriteStartElement("rules");
            int baseAc = SrdHelpers.GetBaseAc(m.ArmorClass);
            w.WriteStartElement("stat");
            w.WriteAttributeString("name",  "companion:ac");
            w.WriteAttributeString("value", baseAc.ToString());
            w.WriteAttributeString("bonus", "base");
            w.WriteEndElement();

            w.WriteStartElement("stat");
            w.WriteAttributeString("name",  "companion:hp:max");
            w.WriteAttributeString("value", m.HitPoints.ToString());
            w.WriteAttributeString("bonus", "base");
            w.WriteEndElement();

            int walkSpeed = SrdHelpers.GetWalkSpeed(m.Speed);
            w.WriteStartElement("stat");
            w.WriteAttributeString("name",  "companion:speed");
            w.WriteAttributeString("value", walkSpeed.ToString());
            w.WriteAttributeString("bonus", "base");
            w.WriteEndElement();

            w.WriteEndElement(); // </rules>
            w.WriteEndElement(); // </element>

            // ── Companion Traits ──────────────────────────────────────────────
            if (m.SpecialAbilities != null)
            {
                for (int i = 0; i < m.SpecialAbilities.Count; i++)
                {
                    WriteChildElement(w, "Companion Trait", monsterSlug, "TRAIT",
                        m.SpecialAbilities[i].Name, m.SpecialAbilities[i].Desc, traitIds[i]);
                }
            }

            // ── Companion Actions ─────────────────────────────────────────────
            if (m.Actions != null)
            {
                for (int i = 0; i < m.Actions.Count; i++)
                {
                    WriteChildElement(w, "Companion Action", monsterSlug, "ACTION",
                        m.Actions[i].Name, m.Actions[i].Desc, actionIds[i]);
                }
            }
        }

        private static void WriteDescriptionElement(XmlWriter w, SrdMonster m)
        {
            w.WriteStartElement("description");
            w.WriteStartElement("p");
            w.WriteAttributeString("class", "flavor");
            w.WriteString($"{SrdHelpers.Capitalize(m.Size ?? "Medium")} {SrdHelpers.Capitalize(m.Type ?? "beast")}, " +
                          $"{m.Alignment ?? "unaligned"}");
            w.WriteEndElement(); // </p>
            w.WriteEndElement(); // </description>
        }

        private static void WriteChildElement(XmlWriter w, string type, string monsterSlug,
            string kind, string name, string desc, string id)
        {
            w.WriteStartElement("element");
            w.WriteAttributeString("name",   name);
            w.WriteAttributeString("type",   type);
            w.WriteAttributeString("source", SourceName);
            w.WriteAttributeString("id",     id);
            w.WriteStartElement("compendium");
            w.WriteAttributeString("display", "false");
            w.WriteEndElement();
            if (!string.IsNullOrWhiteSpace(desc))
            {
                w.WriteStartElement("description");
                w.WriteStartElement("p");
                w.WriteString(desc);
                w.WriteEndElement(); // </p>
                w.WriteEndElement(); // </description>
                w.WriteStartElement("sheet");
                w.WriteStartElement("description");
                w.WriteString(desc);
                w.WriteEndElement(); // </description>
                w.WriteEndElement(); // </sheet>
            }
            w.WriteEndElement(); // </element>
        }

        // ── ID / slug helpers ─────────────────────────────────────────────────

        private static List<string> BuildChildIds(
            List<SrdAbilityOrAction> items, string monsterSlug, string kind)
        {
            if (items == null || items.Count == 0) return new List<string>();

            var ids   = new List<string>();
            var seen  = new HashSet<string>();

            foreach (var item in items)
            {
                string nameSlug = Slugify(item.Name ?? "unnamed");
                string candidate = $"{IdPrefix}_COMPANION_{kind}_{ToIdPart(monsterSlug)}_{ToIdPart(nameSlug)}";

                // Ensure uniqueness if two items share the same normalized name
                string unique = candidate;
                int    n      = 1;
                while (!seen.Add(unique))
                    unique = $"{candidate}_{++n}";

                ids.Add(unique);
            }
            return ids;
        }

        private static string ToIdPart(string slug) =>
            Regex.Replace(slug.ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');

        private static string Slugify(string name) =>
            name?.Trim().ToLowerInvariant().Replace(' ', '-') ?? "unknown";

        private static bool IsFamiliarCandidate(SrdMonster m)
        {
            // Tiny celestials/fey/fiends/undead are typical familiar candidates.
            if (!string.Equals(m.Size, "Tiny", StringComparison.OrdinalIgnoreCase)) return false;
            string t = m.Type?.ToLowerInvariant() ?? "";
            return t is "beast" or "celestial" or "fey" or "fiend" or "undead";
        }

        // ── XML write helpers ─────────────────────────────────────────────────

        private static void WriteElement(XmlWriter w, string name, string value)
        {
            w.WriteStartElement(name);
            w.WriteString(value);
            w.WriteEndElement();
        }

        private static void WriteSet(XmlWriter w, string name, string value)
        {
            w.WriteStartElement("set");
            w.WriteAttributeString("name", name);
            w.WriteString(value ?? "");
            w.WriteEndElement();
        }

        private static HashSet<string> LoadLinkedSlugs(string sqlitePath)
        {
            var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT c.slug FROM creatures c JOIN creature_aurora_links cal ON cal.creature_id = c.creature_id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                slugs.Add(reader.GetString(0));
            return slugs;
        }
    }
}
