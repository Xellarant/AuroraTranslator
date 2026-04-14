using _5eApiTranslator.Models;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
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
using Microsoft.Data.Sqlite;

namespace _5eApiTranslator
{
    class Program
    {        
        static string apiBase = "https://www.dnd5eapi.co/api";
        static string connectionString = "Data Source=(LocalDb)\\MSSQLLocalDB;Initial Catalog=5eHelper;User ID=5eHelper_Admin;pwd=5eHelper_Admin";
        static string projectRootPath = ResolveProjectRootPath();
        static string defaultAuroraPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "5e Character Builder",
            "custom");
        static string defaultSqlitePath = Path.Combine(
            projectRootPath,
            "Data",
            "aurora-character-loading-poc.sqlite");
        static string sqliteSchemaPath = Path.Combine(
            projectRootPath,
            "Data",
            "sqlite-character-loading-poc.sql");

        static async Task Main(string[] args)
        {
            try
            {
                await RunAsync(args);
            }
            catch (Exception ex)
            {
                WriteError(ex, args);
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunAsync(string[] args)
        {
            if (args.Length > 0
                && string.Equals(args[0], "sqlite-import", StringComparison.OrdinalIgnoreCase))
            {
                string auroraPath = args.Length > 1 ? args[1] : defaultAuroraPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultSqlitePath;

                ImportAuroraToSqlite(auroraPath, sqlitePath);
                return;
            }

            Console.WriteLine("Pass `sqlite-import [auroraPath] [sqlitePath]` to import Aurora XML into the SQLite proof of concept schema.");

            //await FillClasses();
            //Console.WriteLine("Classes Imported.");

            //await FillSpells();
            //Console.WriteLine("Spells Imported.");

            //FillAuroraSpells();
            //Console.WriteLine("Aurora Spells Imported.");

        }

        private static void WriteError(Exception exception, string[] args)
        {
            Console.Error.WriteLine("The operation failed.");
            Console.Error.WriteLine(exception.Message);

            for (Exception inner = exception.InnerException; inner != null; inner = inner.InnerException)
            {
                Console.Error.WriteLine(inner.Message);
            }

            if (args.Length > 0
                && string.Equals(args[0], "sqlite-import", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: sqlite-import [auroraPath] [sqlitePath]");
                Console.Error.WriteLine($"Default Aurora path: {defaultAuroraPath}");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
        }

        private static string ResolveProjectRootPath()
        {
            string[] startingPaths = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };

            foreach (string startingPath in startingPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                DirectoryInfo directory = new DirectoryInfo(startingPath);

                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "5eDataGenerator.csproj")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the project root containing 5eDataGenerator.csproj.");
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

                        using (SqlCommand sqlCommand = new SqlCommand("sp_Spells_Classes_Import", sqlConnection))
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

        private static void ImportAuroraToSqlite(string auroraPath, string sqlitePath)
        {
            if (!Directory.Exists(auroraPath))
            {
                throw new DirectoryNotFoundException($"Aurora path was not found: {auroraPath}");
            }

            if (!File.Exists(sqliteSchemaPath))
            {
                throw new FileNotFoundException("The SQLite schema file was not found.", sqliteSchemaPath);
            }

            AuroraImportCatalog catalog = BuildAuroraImportCatalog(auroraPath);
            AuroraSqlitePocImporter.Import(catalog, sqliteSchemaPath, sqlitePath);

            Console.WriteLine($"Imported {catalog.Elements.Count} Aurora elements and {catalog.Spells.Count} Aurora spells into {sqlitePath}.");
        }

        private static AuroraImportCatalog BuildAuroraImportCatalog(string auroraPath)
        {
            string[] files = Directory
                .GetFiles(auroraPath, "*.xml", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AuroraImportCatalog catalog = new();

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(auroraPath, file);
                XDocument xml = XDocument.Load(file);
                var info = xml.Root?.Element("info");

                catalog.Files.Add(new AuroraFileInfo
                {
                    RelativePath = relativePath,
                    Name = info?.Element("name")?.Value ?? Path.GetFileNameWithoutExtension(file),
                    Description = info?.Element("description")?.Value,
                    Author = new Author
                    {
                        name = info?.Element("author")?.Value,
                        url = info?.Element("author")?.Attribute("url")?.Value
                    },
                    FileVersion = new FileVersion
                    {
                        versionString = info?.Element("update")?.Attribute("version")?.Value,
                        fileName = info?.Element("update")?.Element("file")?.Attribute("name")?.Value,
                        fileUrl = info?.Element("update")?.Element("file")?.Attribute("url")?.Value
                    }
                });

                foreach (var element in xml.Root?.Elements("element") ?? Enumerable.Empty<XElement>())
                {
                    string name = element.Attribute("name")?.Value;
                    string source = element.Attribute("source")?.Value;
                    string id = element.Attribute("id")?.Value;
                    string type = element.Attribute("type")?.Value;

                    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
                        continue;

                    if (string.Equals(type, "spell", StringComparison.OrdinalIgnoreCase))
                    {
                        AuroraSpell spell = FillAuroraSpell(element, name, source, id);
                        spell.source_file_path = relativePath;
                        catalog.Spells.Add(spell);
                    }
                    else
                    {
                        AuroraElement auroraElement = FillAuroraElement(element, name, source, id, type);
                        auroraElement.source_file_path = relativePath;
                        catalog.Elements.Add(auroraElement);
                    }
                }
            }

            return catalog;
        }

        private static void GrabAuroraElements()
        {
            string auroraPath = @"C:\Users\Ralla\Documents\5e Character Builder\custom";
            string[] files = Directory.GetFiles(auroraPath, "*.xml", SearchOption.AllDirectories);

            List<AuroraFileInfo> auroraFiles = new();
            List<AuroraSpell> spellsFound = new();
            List<AuroraElement> elementsFound = new();

            List<string> types = new();

            var elementImporters = new Dictionary<string, Action<AuroraElement>>(StringComparer.OrdinalIgnoreCase)
            {
                // already implemented
                { "feat",                       ImportAuroraFeat  },
                { "magic item",                 ImportMagicItem   },

                // own table/procedure
                { "class",                      ImportClass       },
                { "archetype",                  ImportArchetype   },
                { "race",                       ImportRace        },
                { "sub race",                   ImportSubRace     },
                { "background",                 ImportBackground  },
                { "language",                   ImportLanguage    },
                { "proficiency",                ImportProficiency },

                // shared sp_Features_Import
                { "class feature",              ImportFeature     },
                { "archetype feature",          ImportFeature     },
                { "racial trait",               ImportFeature     },
                { "background feature",         ImportFeature     },
                { "feat feature",               ImportFeature     },
                { "ability score improvement",  ImportFeature     },

                // shared sp_Items_Import
                { "item",                       ImportItem        },
                { "weapon",                     ImportItem        },
                { "armor",                      ImportItem        },
                { "ammunition",                 ImportItem        },
                { "mount",                      ImportItem        },
                { "vehicle",                    ImportItem        },

                // lower priority
                { "companion",                  ImportCompanion   },
                { "companion action",           ImportCompanion   },
                { "companion trait",            ImportCompanion   },
                { "deity",                      ImportDeity       },
                { "option",                     ImportOption      },
                { "list",                       ImportList        },
                // "support" intentionally omitted — Aurora meta-type, no user-facing data
            };

            foreach (string file in files)
            {
                FileStream stream = File.OpenRead(file);
                XDocument xml = XDocument.Load(stream);                

                foreach (var node in xml.DescendantNodes())
                {
                    if (node is XElement element
                        && element.Name == "info")
                    {
                        var fileName = element.Element("name")?.Value;
                        var fileDesc = element.Element("description")?.Value;
                        var fileAuthor = new Author();
                        fileAuthor.name = element.Element("author")?.Value;
                        fileAuthor.url = element.Element("author")?.Attribute("url")?.Value;
                        FileVersion fileVersion = new();
                        fileVersion.versionString = element.Element("update")?.Attribute("version")?.Value;
                        fileVersion.fileName = element.Element("update")?.Element("file")?.Attribute("name")?.Value;
                        fileVersion.fileUrl = element.Element("update")?.Element("file")?.Attribute("url")?.Value;

                        if (!auroraFiles.Where(x => x.FileVersion.fileName == fileVersion.fileName).Any())
                        {
                            auroraFiles.Add(new AuroraFileInfo
                            {
                                Name = fileName,
                                Description = fileDesc,
                                Author = fileAuthor,
                                FileVersion = fileVersion
                            });
                        }
                    }

                    else if (node is XElement element1
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
                            {
                                ImportAuroraSpell(spell);
                                spellsFound.Add(spell);
                            }
                        }

                        else if (typeAttribute?.Value != null && elementImporters.TryGetValue(typeAttribute.Value, out var import))
                        {
                            AuroraElement el = FillAuroraElement((XElement)node, nameAttribute.Value, sourceAttribute.Value,
                                idAttribute.Value, typeAttribute.Value);

                            if (el != null)
                            {
                                import(el);
                                elementsFound.Add(el);
                            }
                        }
                    }
                    else if (node is XElement element2)
                    {
                        try
                        {
                            var nameAttribute = element2.Attribute("name");
                            var typeAttribute = element2.Attribute("type");
                            var sourceAttribute = element2.Attribute("source");
                            var idAttribute = element2.Attribute("id");
                            var AuroraElement = FillAuroraElement((XElement)node, nameAttribute?.Value, sourceAttribute?.Value, idAttribute?.Value);

                            if (typeAttribute?.Value.ToLower() == "magic item")
                            {
                                Console.WriteLine("Hey! I found one!");
                            }
                        }
                        catch (Exception ex) 
                        {
                            // SOMETHING WENT WRONG!!!
                            Console.Error.WriteLine(ex.Message); // oh well.
                        }

                        if (!types.Contains((node as XElement)?.Name.ToString()))
                            types.Add((node as XElement)?.Name.ToString());
                    }
                }                
            }
            Console.WriteLine($"Number of types detected: {types.Count}");

            // spells found?
            Console.WriteLine($"Number of spells checked/filled: {spellsFound.Count}");

            // feats found?
            Console.WriteLine($"Number of feats checked/filled: {elementsFound.Where(x => x.type.ToLower() == "feat").Count()}");

            // magic items found?
            Console.WriteLine($"Number of magic items checked/filled: {elementsFound.Where(x => x.type.ToLower() == "magic item").Count()}");
        }

        private static void ImportMagicItem(AuroraElement mitem)
        {
            //throw new NotImplementedException();

            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                using (SqlCommand sqlCommand = new SqlCommand("sp_MItems_Import", sqlConnection))
                {
                    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                    sqlCommand.Parameters.AddWithValue("@mitem_index", mitem.index);
                    sqlCommand.Parameters.AddWithValue("@mitem_name", mitem.name);
                    sqlCommand.Parameters.AddWithValue("@mitem_desc", mitem.description);
                    sqlCommand.Parameters.AddWithValue("@mitemTypeId", null); // TODO: insert logic to determine mitem type category
                    sqlCommand.Parameters.AddWithValue("@mitem_source", mitem.source);
                    sqlCommand.Parameters.AddWithValue("@mitem_aurora_id", mitem.id);
                    if (mitem.requirements?.Count > 0)
                    {
                        sqlCommand.Parameters.AddWithValue("@mitem_requirements", mitem.requirements?.Count > 1 ? string.Join(", ", mitem.requirements) : mitem.requirements[0]);
                    }
                    if (mitem.supports?.Count > 0)
                    {
                        sqlCommand.Parameters.AddWithValue("@mitem_supports", mitem.supports?.Count > 1 ? string.Join("; ", mitem.supports) : mitem.supports[0]);
                    }
                    sqlCommand.Parameters.AddWithValue(
                        "@mitem_rules", mitem.rules != null ?
                            JsonSerializer.Serialize(mitem.rules, new JsonSerializerOptions { IncludeFields = true })
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

                //    //TODO: finish working out below code.

                //    //List<string> mitem_Supports = new List<string>();
                //    //// Rules mitem_Rules = new Rules();

                //    //if (mitem.supports != null)
                //    //    mitem_Supports.AddRange(mitem.supports);

                //    //if (mitem.rules != null)
                //    //{
                //    //    // mitem_Rules = mitem.rules;
                //    //    /*
                //    //     * TODO: will need to deal with Grants, Selects, and Stats.
                //    //     */
                //    //}                    

                //    //using (SqlCommand sqlCommand = new SqlCommand("sp_mitems_Rules_Import", sqlConnection))
                //    //{
                //    //    sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;

                //    //    sqlCommand.Parameters.AddWithValue("@mitem_index", mitem.index);
                //    //    //sqlCommand.Parameters.AddWithValue("@tvpmitems", mitem_Classes?.ToDataTable());

                //    //    sqlConnection.Open();
                //    //    sqlCommand.ExecuteNonQuery();
                //    //    sqlConnection.Close();
                //    //}
            }
        }

        private static AuroraSpell FillAuroraSpell(XElement spellElement, string name, string source, string id)
        {
            var spell = new AuroraSpell();

            spell.name = name;
            spell.source = source;
            spell.aurora_id = id;
            spell.index = BuildSlug(spell.name);

            foreach (var childElement in spellElement.Elements())
            {
                // fill compendium_display
                if (childElement.Name == "compendium")
                {
                    spell.compendium_display = Convert.ToBoolean(childElement.Attribute("display")?.Value ?? "true");
                }

                // fill supports (for now just going into classes)
                if (childElement.Name == "supports")
                {
                    spell.classes = new();

                    AuroraTextCollection supports = ParseAuroraTextCollection(childElement.Value);

                    foreach (var support in supports ?? Enumerable.Empty<string>())
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
                    FillSetters(spell.setters, childElement);
                }
            }

            if (spell.setters != null)
            {
                spell.url = spell.url ?? spell.setters.sourceUrl;

                if (spell.setters.level != 0)
                {
                    spell.level = spell.setters.level;
                }

                if (!string.IsNullOrWhiteSpace(spell.setters.school))
                {
                    spell.school = new BaseApiClass { index = spell.setters.school.ToLower() };
                }

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
            }

            string spellDescription = string.Join(" ", spell.desc ?? new List<string>()).ToLowerInvariant();

            if (spellDescription.Contains("melee spell attack"))
            {
                spell.attack_type = "melee";
            }
            else if (spellDescription.Contains("ranged spell attack"))
            {
                spell.attack_type = "ranged";
            }

            return spell;
        }

        private static AuroraElement FillAuroraElement(XElement element, string name, string source, string id, string type = null)
        {
            var auroraElement = new AuroraElement();

            auroraElement.name = name;
            auroraElement.type = type ?? "auroraElement";
            auroraElement.source = source;
            auroraElement.id = id;
            auroraElement.index = BuildSlug(auroraElement.name);

            foreach (var childElement in element.Elements())
            {
                // fill compendium_display
                if (childElement.Name == "compendium")
                {
                    auroraElement.compendium.display = Convert.ToBoolean(childElement.Attribute("display")?.Value ?? "true");
                }

                // fill supports (for now just going into classes)
                if (childElement.Name == "supports")
                {
                    auroraElement.supports = ParseAuroraTextCollection(childElement.Value);
                }

                // Fill requirements...
                // TODO: figure out what to do with requirements (how to store/retrieve?)
                if (childElement.Name == "requirements")
                {
                    auroraElement.requirements = ParseAuroraTextCollection(childElement.Value);
                }

                if (childElement.Name == "prerequisite")
                {
                    auroraElement.prerequisite = childElement.Value;
                }

                // fill descriptions
                if (childElement.Name == "description")
                {
                    auroraElement.description = childElement.Value;

                    //if (childElement.Value.Contains("At Higher Levels."))
                    //{
                    //    auroraElement.higher_level = new();

                    //    auroraElement.desc.Add(childElement.Value.Substring(0, childElement.Value.IndexOf("At Higher Levels.") - 1));
                    //    auroraElement.higher_level.Add(childElement.Value.Substring(childElement.Value.IndexOf("At Higher Levels.")));
                    //}
                    //else
                    //{
                    //    auroraElement.desc.Add(childElement.Value);
                    //}
                }

                if (childElement.Name == "sheet")
                {
                    auroraElement.sheet = new();

                    if (childElement.Attribute("display") != null)
                    {
                        auroraElement.sheet.display = Convert.ToBoolean(childElement.Attribute("display")?.Value);
                    }
                    auroraElement.sheet.alt = childElement.Attribute("alt")?.Value;
                    auroraElement.sheet.action = childElement.Attribute("action")?.Value;
                    auroraElement.sheet.usage = childElement.Attribute("usage")?.Value;

                    if (childElement.Elements("description")?.Any() == true)
                    {
                        auroraElement.sheet.description = new();
                    }

                    foreach (var desc in childElement.Elements("description"))
                    {
                        auroraElement.sheet.description.Add(
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
                    auroraElement.setters = new();
                    FillSetters(auroraElement.setters, childElement);
                }

                if (childElement.Name == "spellcasting")
                {
                    // used if this element is a spellcasting class or archetype.

                    auroraElement.spellcasting = new();
                    auroraElement.spellcasting.name = childElement.Attribute("name")?.Value;
                    auroraElement.spellcasting.ability = childElement.Attribute("ability")?.Value;
                    auroraElement.spellcasting.prepare = ParseNullableBoolean(childElement.Attribute("prepare")?.Value);
                    auroraElement.spellcasting.allowReplace = ParseNullableBoolean(childElement.Attribute("allowReplace")?.Value);
                    
                    if (childElement.Element("list") != null)
                    {
                        auroraElement.spellcasting.list = ParseAuroraTextCollection(childElement.Element("list")?.Value);
                    }
                    
                    auroraElement.spellcasting.extend = ParseNullableBoolean(childElement.Attribute("extend")?.Value) ?? false;

                    if (childElement.Element("extend") != null)
                    {
                        auroraElement.spellcasting.extendList = ParseAuroraTextCollection(childElement.Element("extend")?.Value);
                    }
                }

                if (childElement.Name == "multiclass")
                {
                    // used for class-type elements.
                    // used to describe what's required to multiclass from or into this class.

                    auroraElement.multiclass = new();
                    auroraElement.multiclass.id = childElement.Attribute("id")?.Value;
                    auroraElement.multiclass.prerequisite = childElement.Element("prerequisite")?.Value;

                    if (childElement.Element("requirements") != null)
                    {
                        auroraElement.multiclass.requirements = ParseAuroraTextCollection(childElement.Element("requirements")?.Value);
                    }

                    XElement mcSetters = childElement.Element("setters");
                    if (mcSetters != null)
                    {
                        auroraElement.multiclass.setters = new();
                        FillSetters(auroraElement.multiclass.setters, mcSetters);
                    }

                    XElement mcRules = childElement.Element("rules");
                    if (mcRules != null)
                    {
                        auroraElement.multiclass.rules = FillRules(mcRules);
                    }

                }

                if (childElement.Name == "rules")
                {
                    auroraElement.rules = FillRules(childElement);
                }
            }

            return auroraElement;
        }

        private static Rules FillRules(XElement parentElement)
        {
            var rules = new Rules
            {
                grants = new(),
                selects = new(),
                stats = new()
            };

            foreach (var grant in parentElement.Elements("grant"))
            {
                rules.grants.Add(new Grant
                {
                    type = grant.Attribute("type")?.Value,
                    id = grant.Attribute("id")?.Value,
                    name = grant.Attribute("name")?.Value,
                    level = grant.Attribute("level")?.Value != null ?
                            Convert.ToInt32(grant.Attribute("level")?.Value) :
                            null,
                    requirements = ParseAuroraTextCollection(grant.Attribute("requirements")?.Value)
                });
            }

            foreach (var select in parentElement.Elements("select"))
            {
                rules.selects.Add(new Select
                {
                    type = select.Attribute("type")?.Value,
                    name = select.Attribute("name")?.Value,
                    supports = ParseAuroraTextCollection(select.Attribute("supports")?.Value),
                    level = select.Attribute("level")?.Value != null ?
                        Convert.ToInt32(select.Attribute("level")?.Value) :
                        null,
                    requirements = ParseAuroraTextCollection(select.Attribute("requirements")?.Value),
                    number = select.Attribute("number")?.Value != null ?
                        Convert.ToInt32(select.Attribute("number")?.Value) :
                        1,
                    defaultChoice = select.Attribute("default")?.Value,
                    optional = ParseNullableBoolean(select.Attribute("optional")?.Value) ?? false,
                    spellcasting = select.Attribute("spellcasting")?.Value
                });
            }

            foreach (var stat in parentElement.Elements("stat"))
            {
                rules.stats.Add(new Stat
                {
                    name = stat.Attribute("name")?.Value,
                    value = stat.Attribute("value")?.Value,
                    bonus = stat.Attribute("bonus")?.Value,
                    equipped = ParseAuroraTextCollection(stat.Attribute("equipped")?.Value),
                    level = stat.Attribute("level")?.Value != null ?
                        Convert.ToInt32(stat.Attribute("level")?.Value) :
                        null,
                    requirements = ParseAuroraTextCollection(stat.Attribute("requirements")?.Value),
                    inline = ParseNullableBoolean(stat.Attribute("inline")?.Value) ?? false,
                    alt = stat.Attribute("alt")?.Value
                });
            }

            return rules;
        }

        private static void FillSetters(AuroraSetters setters, XElement parentElement)
        {
            var settersType = typeof(AuroraSetters);
            var setterProps = settersType.GetProperties().ToList();

            foreach (var setter in parentElement.Elements("set"))
            {
                string setterName = setter.Attribute("name")?.Value;

                if (string.IsNullOrWhiteSpace(setterName))
                    continue;

                var setterEntry = new AuroraSetterEntry
                {
                    name = setterName,
                    value = setter.Value
                };

                foreach (var attribute in setter.Attributes().Where(x => x.Name.LocalName != "name"))
                {
                    setterEntry.attributes[attribute.Name.LocalName] = attribute.Value;
                }

                setters.entries.Add(setterEntry);

                if (string.Equals(setterName, "keywords", StringComparison.OrdinalIgnoreCase))
                {
                    setters.keywords = SplitTopLevel(setter.Value, ',');
                    continue;
                }

                if (string.Equals(setterName, "names", StringComparison.OrdinalIgnoreCase))
                {
                    setters.names ??= new List<Names>();
                    setters.names.Add(new Names
                    {
                        type = setterEntry.GetAttribute("type"),
                        names = SplitTopLevel(setter.Value, ',')
                    });
                    continue;
                }

                if (string.Equals(setterName, "multiclass proficiencies", StringComparison.OrdinalIgnoreCase))
                {
                    setters.multiclass_proficiencies = SplitTopLevel(setter.Value, ',');
                    continue;
                }

                string normalizedSetterName = NormalizeSetterPropertyName(setterName);
                PropertyInfo setterProp = setterProps.FirstOrDefault(
                    x => string.Equals(x.Name, normalizedSetterName, StringComparison.OrdinalIgnoreCase));

                if (setterProp != null)
                {
                    string content = setter.Value;

                    if (setterProp.PropertyType.Equals(typeof(string)))
                    {
                        setterProp.SetValue(setters, content);
                    }
                    else if (!string.IsNullOrWhiteSpace(content))
                    {
                        TypeConverter typeConverter = TypeDescriptor.GetConverter(setterProp.PropertyType);

                        try
                        {
                            setterProp.SetValue(setters, typeConverter.ConvertFromString(content));
                        }
                        catch
                        {
                            // Keep the raw setter entry even when a typed projection does not parse cleanly.
                        }
                    }
                }
            }
        }

        private static AuroraTextCollection ParseAuroraTextCollection(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return null;

            var collection = new AuroraTextCollection
            {
                raw = rawText.Trim()
            };

            collection.AddRange(SplitTopLevel(rawText, ','));

            return collection;
        }

        private static List<string> SplitTopLevel(string input, char separator)
        {
            var values = new List<string>();

            if (string.IsNullOrWhiteSpace(input))
                return values;

            int parenthesesDepth = 0;
            int bracketsDepth = 0;
            int bracesDepth = 0;
            var current = new System.Text.StringBuilder();

            foreach (char ch in input)
            {
                switch (ch)
                {
                    case '(':
                        parenthesesDepth++;
                        break;
                    case ')':
                        parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                        break;
                    case '[':
                        bracketsDepth++;
                        break;
                    case ']':
                        bracketsDepth = Math.Max(0, bracketsDepth - 1);
                        break;
                    case '{':
                        bracesDepth++;
                        break;
                    case '}':
                        bracesDepth = Math.Max(0, bracesDepth - 1);
                        break;
                }

                if (ch == separator
                    && parenthesesDepth == 0
                    && bracketsDepth == 0
                    && bracesDepth == 0)
                {
                    string candidate = current.ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        values.Add(candidate);
                    }

                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            string finalCandidate = current.ToString().Trim();

            if (!string.IsNullOrWhiteSpace(finalCandidate))
            {
                values.Add(finalCandidate);
            }

            return values;
        }

        private static bool? ParseNullableBoolean(string value)
        {
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string NormalizeSetterPropertyName(string setterName)
        {
            return setterName
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        private static string BuildSlug(string value)
        {
            return value?.Trim().ToLower().Replace(" ", "-");
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

                using (SqlCommand sqlCommand = new SqlCommand("sp_Spells_Classes_Import", sqlConnection))
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
                        sqlCommand.Parameters.AddWithValue("@feat_requirements", feat.requirements?.Count > 1 ? string.Join(", ", feat.requirements) : feat.requirements[0]);
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

        // sp_Classes_Import
        // Key params: index, name, source, aurora_id, hit_die (setters.hd),
        //             spellcasting (ability, list), multiclass (prerequisite, requirements)
        //             rules serialized as JSON (level-gated grants/selects)
        private static void ImportClass(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Archetypes_Import
        // Key params: index, name, source, aurora_id, parent_class (from supports),
        //             spellcasting (ability, list, extend), rules serialized as JSON
        private static void ImportArchetype(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Races_Import
        // Key params: index, name, source, aurora_id,
        //             names_male / names_female / names_clan (setters), names_format (setters)
        //             rules serialized as JSON
        private static void ImportRace(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_SubRaces_Import
        // Key params: index, name, source, aurora_id, parent_race (from supports),
        //             rules serialized as JSON
        private static void ImportSubRace(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Backgrounds_Import
        // Key params: index, name, source, aurora_id, description,
        //             rules serialized as JSON (grants for proficiencies/languages/equipment)
        private static void ImportBackground(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Languages_Import
        // Key params: index, name, source, aurora_id,
        //             speakers (setters), script (setters), is_exotic (setters)
        private static void ImportLanguage(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Proficiencies_Import
        // Key params: index, name, source, aurora_id, type (from supports — e.g. skill, weapon, armor, tool)
        private static void ImportProficiency(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Features_Import
        // Shared by: Class Feature, Archetype Feature, Racial Trait,
        //            Background Feature, Feat Feature, Ability Score Improvement
        // Key params: index, name, source, aurora_id, feature_type (el.type),
        //             parent_id (from supports), level (from sheet or rules),
        //             description, sheet info (alt, action, usage),
        //             rules serialized as JSON
        private static void ImportFeature(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Items_Import
        // Shared by: Item, Weapon, Armor, Ammunition, Mount, Vehicle
        // Key params: index, name, source, aurora_id, item_type (el.type),
        //             description, cost (setters), weight (setters),
        //             -- weapon-specific: damage die, damage type, properties (from supports)
        //             -- armor-specific: ac formula, stealth disadvantage (setters)
        //             -- mount/vehicle: speed, capacity (setters)
        private static void ImportItem(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Companions_Import
        // Shared by: Companion, Companion Action, Companion Trait
        // Key params: index, name, source, aurora_id, companion_type (el.type),
        //             description, sheet info — full stat block TBD
        private static void ImportCompanion(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Deities_Import
        // Key params: index, name, source, aurora_id, description,
        //             alignment, domains, symbol (likely all setters)
        private static void ImportDeity(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Options_Import
        // Key params: index, name, source, aurora_id, description,
        //             rules serialized as JSON (modifies character creation)
        private static void ImportOption(AuroraElement el)
        {
            throw new NotImplementedException();
        }

        // sp_Lists_Import
        // Key params: index, name, source, aurora_id, list_type (from supports),
        //             entries serialized as JSON (personality traits, ideals, bonds, flaws)
        private static void ImportList(AuroraElement el)
        {
            throw new NotImplementedException();
        }
    }
}
