using AuroraTranslator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuroraTranslator
{
    /// <summary>
    /// Shared helpers for formatting SRD JSON fields into human-readable strings
    /// suitable for both Aurora XML output and SQLite storage.
    /// </summary>
    internal static class SrdHelpers
    {
        /// <summary>Formats a numeric challenge rating as a fraction string ("1/8", "1/4", "1/2") or integer.</summary>
        public static string FormatCr(double cr)
        {
            if (cr == 0.125) return "1/8";
            if (cr == 0.25)  return "1/4";
            if (cr == 0.5)   return "1/2";
            return ((int)cr).ToString();
        }

        /// <summary>
        /// Formats the AC list into a display string like "15 (chain mail)" or "13, 16 (shield)".
        /// Returns <paramref name="fallback"/> when the list is null or empty.
        /// </summary>
        public static string FormatAc(List<SrdArmorClass> acList, string fallback = null)
        {
            if (acList == null || acList.Count == 0) return fallback;

            var parts = new List<string>();
            foreach (var ac in acList)
            {
                string detail = ac.Type switch
                {
                    "natural"   => $"{ac.Value} (natural armor)",
                    "armor"     => ac.Armor?.Count > 0
                                   ? $"{ac.Value} ({string.Join(", ", ac.Armor.Select(a => a.Name))})"
                                   : ac.Value.ToString(),
                    "spell"     => ac.Spell != null ? $"{ac.Value} (with {ac.Spell.Name})" : ac.Value.ToString(),
                    "condition" => ac.Condition != null ? $"{ac.Value} ({ac.Condition.Name})" : ac.Value.ToString(),
                    _           => ac.Value.ToString()
                };
                parts.Add(detail);
            }
            return string.Join(", ", parts);
        }

        /// <summary>Returns the numeric value of the first AC entry, or 10 if the list is empty.</summary>
        public static int GetBaseAc(List<SrdArmorClass> acList) =>
            acList?.FirstOrDefault()?.Value ?? 10;

        /// <summary>Formats HP as "avg (roll)" or just the average when no roll formula is present.</summary>
        public static string FormatHp(int avg, string roll)
        {
            if (string.IsNullOrWhiteSpace(roll)) return avg.ToString();
            return $"{avg} ({roll})";
        }

        /// <summary>
        /// Formats the speed dictionary into "30 ft., Fly 60 ft." etc.
        /// Walk speed is listed first without a label; other modes are capitalized.
        /// Zero-speed entries ("0 ft.") are omitted.
        /// Returns <paramref name="fallback"/> when the dictionary is null, empty, or all entries are zero.
        /// </summary>
        public static string FormatSpeed(Dictionary<string, JsonElement> speed, string fallback = null)
        {
            if (speed == null || speed.Count == 0) return fallback;

            var parts = new List<string>();
            foreach (var (key, val) in speed)
            {
                string valStr = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                if (string.IsNullOrWhiteSpace(valStr) || valStr == "0 ft.") continue;

                if (string.Equals(key, "walk", StringComparison.OrdinalIgnoreCase))
                    parts.Insert(0, valStr);
                else
                    parts.Add($"{Capitalize(key)} {valStr}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : fallback;
        }

        /// <summary>Extracts the numeric walk speed in feet, or 0 if not present.</summary>
        public static int GetWalkSpeed(Dictionary<string, JsonElement> speed)
        {
            if (speed == null) return 0;
            if (speed.TryGetValue("walk", out var val))
            {
                string s = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                var m = Regex.Match(s ?? "", @"\d+");
                if (m.Success) return int.Parse(m.Value);
            }
            return 0;
        }

        /// <summary>Formats saving throw proficiencies as "STR +3, DEX +5" etc., or null if none.</summary>
        public static string FormatSavingThrows(List<SrdProficiencyEntry> profs)
        {
            if (profs == null) return null;
            var saves = profs
                .Where(p => p.Proficiency?.Index?.StartsWith("saving-throw-") == true)
                .Select(p =>
                {
                    string ab = p.Proficiency.Index["saving-throw-".Length..].ToUpper();
                    return $"{ab} +{p.Value}";
                })
                .ToList();
            return saves.Count > 0 ? string.Join(", ", saves) : null;
        }

        /// <summary>Formats skill proficiencies as "Perception +5, Stealth +3" etc., or null if none.</summary>
        public static string FormatSkills(List<SrdProficiencyEntry> profs)
        {
            if (profs == null) return null;
            var skills = profs
                .Where(p => p.Proficiency?.Index?.StartsWith("skill-") == true)
                .Select(p =>
                {
                    string raw  = p.Proficiency.Index["skill-".Length..];
                    string name = string.Concat(raw.Split('-').Select(w => Capitalize(w)));
                    return $"{name} +{p.Value}";
                })
                .ToList();
            return skills.Count > 0 ? string.Join(", ", skills) : null;
        }

        /// <summary>
        /// Formats the senses dictionary as "Darkvision 60 ft.; Passive Perception 12" etc.
        /// Returns null if the dictionary is null or empty.
        /// </summary>
        public static string FormatSenses(Dictionary<string, JsonElement> senses)
        {
            if (senses == null || senses.Count == 0) return null;

            var parts = new List<string>();
            foreach (var (key, val) in senses)
            {
                if (string.Equals(key, "passive_perception", StringComparison.OrdinalIgnoreCase))
                {
                    string pp = val.ValueKind == JsonValueKind.Number
                        ? val.GetInt32().ToString()
                        : val.ToString();
                    parts.Add($"Passive Perception {pp}");
                    continue;
                }
                string valStr = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                string label  = Capitalize(key.Replace('_', ' '));
                parts.Add($"{label} {valStr}");
            }
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        /// <summary>Capitalizes the first character of a string; returns the string unchanged if null or empty.</summary>
        internal static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    }
}

