using _5eApiTranslator.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.ComponentModel;
using System.Xml.Linq;
using _5eApiTranslator.ResponseObjects;

namespace _5eApiTranslator
{
    class Program
    {        
        static string apiBase = "https://www.dnd5eapi.co/api";
        static string connectionString = "Data Source=(LocalDb)\\MSSQLLocalDB;Initial Catalog=5eHelper;Integrated Security=true";

        static async Task Main(string[] args)
        {            
            Console.WriteLine("Hello World!");

            //await FillClasses();
            //Console.WriteLine("Classes Imported.");

            //await FillSpells();
            //Console.WriteLine("Spells Imported.");

            //FillAuroraSpells();
            //Console.WriteLine("Aurora Spells Imported.");

            GrabAuroraElements();
        }

        private static async Task FillClasses()
        {
            HttpClient client = new HttpClient();

            using (var response = await client.GetAsync($"{apiBase}/classes"))
            {
                string apiResponse = await response.Content.ReadAsStringAsync();
                BulkApiResponse classList = JsonSerializer.Deserialize<BulkApiResponse>(apiResponse, new JsonSerializerOptions { IncludeFields = true });

                using (SqlConnection sqlConnection = new SqlConnection(connectionString))
                {
                    using (SqlCommand sqlCommand = new SqlCommand("sp_Classes_Import", sqlConnection))
                    {
                        sqlConnection.Open();

                        sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                        sqlCommand.Parameters.AddWithValue("@tvpClasses_Basic", classList.results.ToDataTable());
                        sqlCommand.Parameters.AddWithValue("@IsSubclass", true);
                        sqlCommand.ExecuteNonQuery();

                        sqlConnection.Close();
                    }
                }
            }
        }

        private static async Task FillSpells()
        {
            HttpClient client = new HttpClient();

            using (var response = await client.GetAsync($"{apiBase}/spells"))
            {
                string apiResponse = await response.Content.ReadAsStringAsync();
                BulkApiResponse spellList = JsonSerializer.Deserialize<BulkApiResponse>(apiResponse, new JsonSerializerOptions { IncludeFields = true });

                List<Spell> spellListDetailed = new List<Spell>();

                foreach (var entry in spellList.results)
                {
                    using (var spellResponse = await client.GetAsync($"{apiBase}/spells/{entry.index}"))
                    {
                        var spellResponseContent = await spellResponse.Content.ReadAsStringAsync();
                        Spell detailedSpell = JsonSerializer.Deserialize<Spell>(spellResponseContent, new JsonSerializerOptions { IncludeFields = true });
                        spellListDetailed.Add(detailedSpell);
                    }
                }

                using (SqlConnection sqlConnection = new SqlConnection(connectionString))
                {
                    foreach (var spell in spellListDetailed)
                    {
                        using (SqlCommand sqlCommand = new SqlCommand("sp_Spells_Import", sqlConnection))
                        {                            
                            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                            sqlCommand.Parameters.AddWithValue("@spell_index", spell.index);
                            sqlCommand.Parameters.AddWithValue("@spell_name", spell.name);
                            sqlCommand.Parameters.AddWithValue("@spell_desc", spell.desc?.Count > 0 ? string.Join(" \n ", spell.desc) : null);
                            sqlCommand.Parameters.AddWithValue("@higher_level_desc", spell.higher_level?.Count > 0 ? string.Join(" \n ", spell.higher_level) : null);
                            sqlCommand.Parameters.AddWithValue("@spell_range", spell.range);
                            sqlCommand.Parameters.AddWithValue("@hasVerbal", spell.hasVerbal);
                            sqlCommand.Parameters.AddWithValue("@hasSomatic", spell.hasSomatic);
                            sqlCommand.Parameters.AddWithValue("@hasMaterial", spell.hasMaterial);
                            sqlCommand.Parameters.AddWithValue("@material_component", spell.material);
                            sqlCommand.Parameters.AddWithValue("@isRitual", spell.ritual);
                            sqlCommand.Parameters.AddWithValue("@spell_duration", spell.duration);
                            sqlCommand.Parameters.AddWithValue("@isConcentration", spell.concentration);
                            sqlCommand.Parameters.AddWithValue("@casting_time", spell.casting_time);
                            sqlCommand.Parameters.AddWithValue("@spell_level", spell.level);
                            sqlCommand.Parameters.AddWithValue("@spell_attack_type", spell.attack_type);
                            sqlCommand.Parameters.AddWithValue("@spell_damage_type", spell.damage?.damage_type?.index);
                            sqlCommand.Parameters.AddWithValue("@spell_damage_formula", 
                                JsonSerializer.Serialize(spell.damage?.damage_at_slot_level, new JsonSerializerOptions { IncludeFields = true }));
                            sqlCommand.Parameters.AddWithValue("@spell_dc", spell.dc?.index);
                            sqlCommand.Parameters.AddWithValue("@spell_dc_success", spell.dc?.dc_success);
                            sqlCommand.Parameters.AddWithValue("@school_of_magic", spell.school?.index);
                            sqlCommand.Parameters.AddWithValue("@apiUrl", spell.url);

                            foreach (SqlParameter parameter in sqlCommand.Parameters)
                            {
                                if (parameter.Value == null)
                                    parameter.Value = DBNull.Value;
                            }

                            sqlConnection.Open();
                            sqlCommand.ExecuteNonQuery();
                            sqlConnection.Close();
                        }

                        List<BaseApiClass> spell_Classes = new List<BaseApiClass>();
                        spell_Classes.AddRange(spell.classes);
                        spell_Classes.AddRange(spell.subclasses);

                        using (SqlCommand sqlCommand = new SqlCommand("sp_Spell_Classes_Import", sqlConnection))
                        {
                            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                            sqlCommand.Parameters.AddWithValue("@spell_index", spell.index);
                            sqlCommand.Parameters.AddWithValue("@tvpClasses", spell_Classes?.ToDataTable());

                            sqlConnection.Open();
                            sqlCommand.ExecuteNonQuery();
                            sqlConnection.Close();
                        }
                    }                    
                }
            }
        }

        private static void GrabAuroraElements()
        {
            string auroraPath = @"C:\Users\Ralla\Documents\5e Character Builder\custom";
            string[] files = Directory.GetFiles(auroraPath, "*.xml", SearchOption.AllDirectories);            

            foreach (string file in files)
            {
                FileStream stream = File.OpenRead(file);
                XDocument xml = XDocument.Load(stream);

                foreach (var node in xml.DescendantNodes())
                {
                    if (node is XElement element1
                        && element1.Name == "element")
                    {
                        //var element1 = element1;
                        var nameAttribute = element1.Attribute("name");
                        var typeAttribute = element1.Attribute("type");
                        var sourceAttribute = element1.Attribute("source");
                        var idAttribute = element1.Attribute("id");

                        if (typeAttribute?.Value.ToLower() == "spell")
                        {
                            AuroraSpell spell = FillAuroraSpell((XElement)node, nameAttribute.Value, sourceAttribute.Value, idAttribute.Value);
                            
                            if (spell != null)
                                ImportAuroraSpell(spell);
                        }

                        if (typeAttribute?.Value.ToLower() == "feat")
                        {
                            AuroraElement feat = FillAuroraFeat((XElement)node, nameAttribute.Value, sourceAttribute.Value, idAttribute.Value);

                            if (feat != null)
                                ImportAuroraFeat(feat);
                        }
                    }
                }
            }
        }        

        private static AuroraElement FillAuroraFeat(XElement featElement, string name, string source, string id)
        {
            var feat = new AuroraElement();

            feat.name = name;
            feat.type = "Feat";
            feat.source = source;
            feat.id = id;
            feat.index = feat.name.ToLower().Replace(" ", "-");

            foreach (var childElement in featElement.Elements())
            {
                // fill compendium_display
                if (childElement.Name == "compendium")
                {
                    feat.compendium.display = Convert.ToBoolean(childElement.Attribute("display")?.Value ?? "true");
                }

                // fill supports (for now just going into classes)
                if (childElement.Name == "supports")
                {
                    feat.supports = new();

                    List<string> supports = childElement.Value.Split(",").
                        Select(x => x.ToLower().Replace(" ", "-").Trim()).ToList();

                    feat.supports.AddRange(supports);
                }

                // Fill requirements...
                // TODO: figure out what to do with requirements (how to store/retrieve?)
                if (childElement.Name == "requirements")
                {
                    feat.requirements = new();

                    List<string> requirements = childElement.Value.Split(",").
                        Select(x => x.Trim()).ToList();

                    if (feat.supports == null)
                        feat.supports = new();

                    feat.supports.AddRange(requirements);
                }

                // fill descriptions
                if (childElement.Name == "description")
                {
                    feat.description = childElement.Value;

                    //if (childElement.Value.Contains("At Higher Levels."))
                    //{
                    //    feat.higher_level = new();

                    //    feat.desc.Add(childElement.Value.Substring(0, childElement.Value.IndexOf("At Higher Levels.") - 1));
                    //    feat.higher_level.Add(childElement.Value.Substring(childElement.Value.IndexOf("At Higher Levels.")));
                    //}
                    //else
                    //{
                    //    feat.desc.Add(childElement.Value);
                    //}
                }

                if (childElement.Name == "sheet")
                {
                    feat.sheet = new();

                    if (childElement.Attribute("display") != null)
                    {
                        feat.sheet.display = Convert.ToBoolean(childElement.Attribute("display")?.Value);
                    }                    
                    feat.sheet.alt = childElement.Attribute("alt")?.Value;
                    feat.sheet.action = childElement.Attribute("action")?.Value;
                    feat.sheet.usage = childElement.Attribute("usage")?.Value;

                    if (childElement.Elements("description")?.Any() == true)
                    {
                        feat.sheet.description = new();
                    }

                    foreach (var desc in childElement.Elements("description"))
                    {
                        feat.sheet.description.Add(
                            new Description
                            {
                                level = desc.Attribute("level")?.Value != null ?
                                    Convert.ToInt32(desc.Attribute("level")?.Value)
                                    : null,
                                text = desc.Value
                            });
                    }
                }

                // fill setters
                if (childElement.Name == "setters")
                {
                    feat.setters = new();
                    var settersType = typeof(AuroraSetters);
                    var setterProps = settersType.GetProperties().ToList();

                    foreach (var setter in ((XElement)childElement).Elements("set"))
                    {
                        string setterName = setter.Attribute("name").Value;

                        PropertyInfo setterProp = setterProps.FirstOrDefault(x => x.Name == setterName);

                        if (setterProp != null)
                        {
                            string content = setter.Value;
                            TypeConverter typeConverter = TypeDescriptor.GetConverter(setterProp.PropertyType);

                            if (setterProp.PropertyType.Equals(typeof(string)))
                            {
                                setterProp.SetValue(feat.setters, content);
                            }
                            else if (setterName == "keywords")
                            {
                                feat.setters.keywords = new();
                                feat.setters.keywords.AddRange(setter.Value.Split(",").ToList());
                            }
                            else
                            {
                                setterProp.SetValue(feat.setters, typeConverter.ConvertFromString(content));
                            }
                        }
                    }
                }

                // fill FROM setters.
                if (feat.setters != null)
                {
                    // do i do... ANYTHING here?

                    //feat.url = feat.url ?? feat.setters.sourceUrl;

                    //if (feat.setters.level != 0)
                    //{
                    //    feat.level = feat.setters.level;
                    //}

                    //feat.school = new BaseApiClass { index = feat.setters.school.ToLower() };
                    //feat.casting_time = feat.setters.time;
                    //feat.duration = feat.setters.duration;
                    //feat.range = feat.setters.range;

                    //if (feat.components == null)
                    //    feat.components = new();

                    //if (feat.setters.hasVerbalComponent)
                    //{
                    //    feat.components.Add("V");
                    //}
                    //if (feat.setters.hasSomaticComponent)
                    //{
                    //    feat.components.Add("S");
                    //}
                    //if (feat.setters.hasMaterialComponent)
                    //{
                    //    feat.components.Add("M");
                    //}

                    //feat.material = feat.setters.materialComponent;
                    //feat.concentration = feat.setters.isConcentration;
                    //feat.ritual = feat.setters.isRitual;
                    //feat.attack_type = feat.desc.Contains("melee spell attack") && feat.attack_type != null ? "melee" : null;
                    //feat.attack_type = feat.desc.Contains("ranged spell attack") && feat.attack_type != null ? "ranged" : null;

                }

                if (childElement.Name == "spellcasting")
                {
                    // skip this for now. Feats don't have this.
                }

                if (childElement.Name == "multiclass")
                {
                    // skip this for now. Feats don't have this.
                }

                if (childElement.Name == "rules")
                {
                    feat.rules = new();
                    feat.rules.grants = new();
                    feat.rules.selects = new();

                    foreach (var grant in childElement.Elements("grant"))
                    {
                        feat.rules.grants.Add(new Grant
                        {
                            type = grant.Attribute("type")?.Value,
                            id = grant.Attribute("id")?.Value,
                            name = grant.Attribute("name")?.Value,
                            level = grant.Attribute("level")?.Value != null ?
                                    Convert.ToInt32(grant.Attribute("level")?.Value) :
                                    null,
                            requirements = grant.Attribute("requirements")?.Value.Split(",")
                                .Select(x => x.Trim()).ToList()
                        });
                    }

                    foreach (var select in childElement.Elements("select"))
                    {
                        feat.rules.selects.Add(new Select
                        {
                            type = select.Attribute("type")?.Value,
                            name = select.Attribute("name")?.Value,
                            supports = select.Attribute("supports")?.Value
                                .Split(",").Select(x => x.Trim()).ToList(),
                            level = select.Attribute("level")?.Value != null ?
                                Convert.ToInt32(select.Attribute("level")?.Value) :
                                null,
                            requirements = select.Attribute("requirements")?.Value
                                .Split(",").Select(x => x.Trim()).ToList(),
                        });
                    }
                }
            }

            return feat;
        }

        private static AuroraSpell FillAuroraSpell(XElement spellElement, string name, string source, string id)
        {
            var spell = new AuroraSpell();

            spell.name = name;
            spell.source = source;
            spell.aurora_id = id;
            spell.index = spell.name.ToLower().Replace(" ", "-");

            foreach (var childElement in spellElement.Elements())
            {
                // fill compendium_display
                if (childElement.Name == "compendium")
                {
                    spell.compendium_display = Convert.ToBoolean(childElement.Attribute("display").Value);
                }

                // fill supports (for now just going into classes)
                if (childElement.Name == "supports")
                {
                    spell.classes = new();

                    List<string> supports = childElement.Value.Split(",").Select(x => x.Trim()).ToList();

                    foreach (var support in supports)
                        spell.classes.Add(new BaseApiClass { name = support, index = support.ToLower().Replace(" ", "-") });
                }

                // fill descriptions
                if (childElement.Name == "description")
                {
                    spell.desc = new();
                    if (childElement.Value.Contains("At Higher Levels."))
                    {
                        spell.higher_level = new();

                        spell.desc.Add(childElement.Value.Substring(0, childElement.Value.IndexOf("At Higher Levels.") - 1));
                        spell.higher_level.Add(childElement.Value.Substring(childElement.Value.IndexOf("At Higher Levels.")));
                    }
                    else
                    {
                        spell.desc.Add(childElement.Value);
                    }
                }

                // fill setters
                if (childElement.Name == "setters")
                {
                    spell.setters = new();
                    var settersType = typeof(AuroraSetters);
                    var setterProps = settersType.GetProperties().ToList();

                    foreach (var setter in ((XElement)childElement).Elements("set"))
                    {
                        string setterName = setter.Attribute("name").Value;

                        PropertyInfo setterProp = setterProps.FirstOrDefault(x => x.Name == setterName);

                        if (setterProp != null)
                        {
                            string content = setter.Value;
                            TypeConverter typeConverter = TypeDescriptor.GetConverter(setterProp.PropertyType);

                            if (setterProp.PropertyType.Equals(typeof(string)))
                            {
                                setterProp.SetValue(spell.setters, content);
                            }
                            else if (setterName == "keywords")
                            {
                                spell.setters.keywords = new();
                                spell.setters.keywords.AddRange(setter.Value.Split(",").ToList());
                            }
                            else
                            {
                                setterProp.SetValue(spell.setters, typeConverter.ConvertFromString(content));
                            }
                        }
                    }
                }

                // fill FROM setters.
                if (spell.setters != null)
                {
                    spell.url = spell.url ?? spell.setters.sourceUrl;

                    if (spell.setters.level != 0)
                    {
                        spell.level = spell.setters.level;
                    }

                    spell.school = new BaseApiClass { index = spell.setters.school.ToLower() };
                    spell.casting_time = spell.setters.time;
                    spell.duration = spell.setters.duration;
                    spell.range = spell.setters.range;

                    if (spell.components == null)
                        spell.components = new();

                    if (spell.setters.hasVerbalComponent)
                    {
                        spell.components.Add("V");
                    }
                    if (spell.setters.hasSomaticComponent)
                    {
                        spell.components.Add("S");
                    }
                    if (spell.setters.hasMaterialComponent)
                    {
                        spell.components.Add("M");
                    }

                    spell.material = spell.setters.materialComponent;
                    spell.concentration = spell.setters.isConcentration;
                    spell.ritual = spell.setters.isRitual;
                    spell.attack_type = spell.desc.Contains("melee spell attack") && spell.attack_type != null ? "melee" : null;
                    spell.attack_type = spell.desc.Contains("ranged spell attack") && spell.attack_type != null ? "ranged" : null;
                    
                }
            }

            return spell;
        }

        private static void ImportAuroraSpell(AuroraSpell spell)
        {
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                using (SqlCommand sqlCommand = new SqlCommand("sp_Spells_Import", sqlConnection))
                {
                    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                    sqlCommand.Parameters.AddWithValue("@spell_index", spell.index);
                    sqlCommand.Parameters.AddWithValue("@spell_name", spell.name);
                    sqlCommand.Parameters.AddWithValue("@spell_desc", spell.desc?.Count > 0 ? string.Join(" \n ", spell.desc) : null);
                    sqlCommand.Parameters.AddWithValue("@higher_level_desc", spell.higher_level?.Count > 0 ? string.Join(" \n ", spell.higher_level) : null);
                    sqlCommand.Parameters.AddWithValue("@spell_range", spell.range);
                    sqlCommand.Parameters.AddWithValue("@hasVerbal", spell.hasVerbal);
                    sqlCommand.Parameters.AddWithValue("@hasSomatic", spell.hasSomatic);
                    sqlCommand.Parameters.AddWithValue("@hasMaterial", spell.hasMaterial);
                    sqlCommand.Parameters.AddWithValue("@material_component", spell.material);
                    sqlCommand.Parameters.AddWithValue("@isRitual", spell.ritual);
                    sqlCommand.Parameters.AddWithValue("@spell_duration", spell.duration);
                    sqlCommand.Parameters.AddWithValue("@isConcentration", spell.concentration);
                    sqlCommand.Parameters.AddWithValue("@casting_time", spell.casting_time);
                    sqlCommand.Parameters.AddWithValue("@spell_level", spell.level);
                    sqlCommand.Parameters.AddWithValue("@spell_attack_type", spell.attack_type);
                    sqlCommand.Parameters.AddWithValue("@spell_damage_type", spell.damage?.damage_type?.index);
                    sqlCommand.Parameters.AddWithValue("@spell_damage_formula",
                        JsonSerializer.Serialize(spell.damage?.damage_at_slot_level, new JsonSerializerOptions { IncludeFields = true }));
                    sqlCommand.Parameters.AddWithValue("@spell_dc", spell.dc?.index);
                    sqlCommand.Parameters.AddWithValue("@spell_dc_success", spell.dc?.dc_success);
                    sqlCommand.Parameters.AddWithValue("@school_of_magic", spell.school?.index);
                    sqlCommand.Parameters.AddWithValue("@apiUrl", spell.url ?? spell.setters.sourceUrl);
                    sqlCommand.Parameters.AddWithValue("@spell_source", spell.source);
                    sqlCommand.Parameters.AddWithValue("@spell_aurora_id", spell.aurora_id);

                    foreach (SqlParameter parameter in sqlCommand.Parameters)
                    {
                        if (parameter.Value == null)
                            parameter.Value = DBNull.Value;
                    }

                    sqlConnection.Open();
                    sqlCommand.ExecuteNonQuery();
                    sqlConnection.Close();
                }

                List<BaseApiClass> spell_Classes = new List<BaseApiClass>();
                
                if (spell.classes != null)
                    spell_Classes.AddRange(spell.classes);

                if (spell.subclasses != null)
                    spell_Classes.AddRange(spell.subclasses);

                using (SqlCommand sqlCommand = new SqlCommand("sp_Spell_Classes_Import", sqlConnection))
                {
                    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                    sqlCommand.Parameters.AddWithValue("@spell_index", spell.index);
                    sqlCommand.Parameters.AddWithValue("@tvpClasses", spell_Classes?.ToDataTable());

                    sqlConnection.Open();
                    sqlCommand.ExecuteNonQuery();
                    sqlConnection.Close();
                }
            }            
        }

        private static void ImportAuroraFeat(AuroraElement feat)
        {
            //throw new NotImplementedException();

            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                using (SqlCommand sqlCommand = new SqlCommand("sp_Feats_Import", sqlConnection))
                {
                    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                    sqlCommand.Parameters.AddWithValue("@feat_index", feat.index);
                    sqlCommand.Parameters.AddWithValue("@feat_name", feat.name);
                    sqlCommand.Parameters.AddWithValue("@feat_desc", feat.description);
                    sqlCommand.Parameters.AddWithValue("@featTypeId", null); // TODO: insert logic to determine feat type category
                    sqlCommand.Parameters.AddWithValue("@feat_source", feat.source);
                    sqlCommand.Parameters.AddWithValue("@feat_aurora_id", feat.id);
                    if (feat.requirements?.Count > 0)
                    {
                        sqlCommand.Parameters.AddWithValue("@feat_requirements", feat.requirements?.Count > 1 ? string.Join("; ", feat.requirements) : feat.requirements[0]);
                    }
                    if (feat.supports?.Count > 0)
                    {
                        sqlCommand.Parameters.AddWithValue("@feat_supports", feat.supports?.Count > 1 ? string.Join("; ", feat.supports) : feat.supports[0]);
                    }
                    sqlCommand.Parameters.AddWithValue(
                        "@feat_rules", feat.rules != null ? 
                            JsonSerializer.Serialize(feat.rules, new JsonSerializerOptions { IncludeFields = true }) 
                            : null);

                    foreach (SqlParameter parameter in sqlCommand.Parameters)
                    {
                        if (parameter.Value == null)
                            parameter.Value = DBNull.Value;
                    }

                    sqlConnection.Open();
                    sqlCommand.ExecuteNonQuery();
                    sqlConnection.Close();
                }

                //TODO: finish working out below code.

                //List<string> feat_Supports = new List<string>();
                //// Rules feat_Rules = new Rules();

                //if (feat.supports != null)
                //    feat_Supports.AddRange(feat.supports);

                //if (feat.rules != null)
                //{
                //    // feat_Rules = feat.rules;
                //    /*
                //     * TODO: will need to deal with Grants, Selects, and Stats.
                //     */
                //}                    

                //using (SqlCommand sqlCommand = new SqlCommand("sp_Feats_Rules_Import", sqlConnection))
                //{
                //    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                //    sqlCommand.Parameters.AddWithValue("@feat_index", feat.index);
                //    //sqlCommand.Parameters.AddWithValue("@tvpFeats", feat_Classes?.ToDataTable());

                //    sqlConnection.Open();
                //    sqlCommand.ExecuteNonQuery();
                //    sqlConnection.Close();
                //}
            }
        }
    }
}
