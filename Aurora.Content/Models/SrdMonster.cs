using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraTranslator.Models
{
    public class SrdMonster
    {
        [JsonPropertyName("index")]
        public string Index { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("size")]
        public string Size { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("subtype")]
        public string Subtype { get; set; }

        [JsonPropertyName("alignment")]
        public string Alignment { get; set; }

        [JsonPropertyName("armor_class")]
        public List<SrdArmorClass> ArmorClass { get; set; }

        [JsonPropertyName("hit_points")]
        public int HitPoints { get; set; }

        [JsonPropertyName("hit_points_roll")]
        public string HitPointsRoll { get; set; }

        // Speed values are strings ("30 ft.") keyed by movement type ("walk", "fly", etc.)
        [JsonPropertyName("speed")]
        public Dictionary<string, JsonElement> Speed { get; set; }

        [JsonPropertyName("strength")]
        public int Strength { get; set; }

        [JsonPropertyName("dexterity")]
        public int Dexterity { get; set; }

        [JsonPropertyName("constitution")]
        public int Constitution { get; set; }

        [JsonPropertyName("intelligence")]
        public int Intelligence { get; set; }

        [JsonPropertyName("wisdom")]
        public int Wisdom { get; set; }

        [JsonPropertyName("charisma")]
        public int Charisma { get; set; }

        [JsonPropertyName("proficiencies")]
        public List<SrdProficiencyEntry> Proficiencies { get; set; }

        [JsonPropertyName("damage_vulnerabilities")]
        public List<string> DamageVulnerabilities { get; set; }

        [JsonPropertyName("damage_resistances")]
        public List<string> DamageResistances { get; set; }

        [JsonPropertyName("damage_immunities")]
        public List<string> DamageImmunities { get; set; }

        [JsonPropertyName("condition_immunities")]
        public List<SrdConditionImmunity> ConditionImmunities { get; set; }

        // Senses values are mixed: strings ("120 ft.") or ints (passive_perception: 20).
        [JsonPropertyName("senses")]
        public Dictionary<string, JsonElement> Senses { get; set; }

        [JsonPropertyName("languages")]
        public string Languages { get; set; }

        [JsonPropertyName("challenge_rating")]
        public double ChallengeRating { get; set; }

        [JsonPropertyName("proficiency_bonus")]
        public int ProficiencyBonus { get; set; }

        [JsonPropertyName("special_abilities")]
        public List<SrdAbilityOrAction> SpecialAbilities { get; set; }

        [JsonPropertyName("actions")]
        public List<SrdAbilityOrAction> Actions { get; set; }
    }

    public class SrdArmorClass
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("value")]
        public int Value { get; set; }

        // Optional references for spell-based or item-based AC.
        [JsonPropertyName("spell")]
        public SrdNamedRef Spell { get; set; }

        // Armor is an array when present (e.g. chain-mail + shield).
        [JsonPropertyName("armor")]
        public List<SrdNamedRef> Armor { get; set; }

        [JsonPropertyName("condition")]
        public SrdNamedRef Condition { get; set; }
    }

    public class SrdProficiencyEntry
    {
        [JsonPropertyName("value")]
        public int Value { get; set; }

        [JsonPropertyName("proficiency")]
        public SrdNamedRef Proficiency { get; set; }
    }

    public class SrdConditionImmunity
    {
        [JsonPropertyName("index")]
        public string Index { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class SrdNamedRef
    {
        [JsonPropertyName("index")]
        public string Index { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class SrdAbilityOrAction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("desc")]
        public string Desc { get; set; }
    }
}

