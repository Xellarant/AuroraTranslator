using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    public class AuroraSetters
    {
        public List<AuroraSetterEntry> entries { get; set; } = new();
        public string sourceUrl { get; set; }
        public string @short { get; set; }
        public List<string> keywords { get; set; }
        public List<Names> names { get; set; }
        public string names_format { get; set; } // says which types go in which order.
        public string hd { get; set; }
        public string hit_die { 
            get
            {
                return hd;
            }
            set
            {
                hd = value;
            }
        } // alias for hd.
        public int level { get; set; }
        public string school { get; set; }
        public string time { get; set; }
        public string duration { get; set; }
        public string range { get; set; }
        public string speakers { get; set; }
        public string script { get; set; }
        public bool standard { get; set; }
        public bool exotic { get; set; }
        public bool secret { get; set; }
        public bool allow_duplicate { get; set; }
        public string cost { get; set; }
        public string weight { get; set; }
        public string speed { get; set; }
        public string capacity { get; set; }
        public bool hasVerbalComponent { get; set; }
        public bool hasSomaticComponent { get; set; }
        public bool hasMaterialComponent { get; set; }
        public string materialComponent { get; set; }
        public bool isConcentration { get; set; }
        public bool isRitual { get; set; }
        public List<string> multiclass_proficiencies { get; set; }

        public IEnumerable<AuroraSetterEntry> FindEntries(string setterName)
        {
            return entries.Where(x => string.Equals(x.name, setterName, StringComparison.OrdinalIgnoreCase));
        }

        public AuroraSetterEntry FindEntry(string setterName)
        {
            return FindEntries(setterName).FirstOrDefault();
        }

        public string GetValue(string setterName)
        {
            return FindEntry(setterName)?.value;
        }

        public bool? GetBoolean(string setterName)
        {
            var value = GetValue(setterName);

            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }

    public class Names
    {
        public string type { get; set; }
        public List<string> names { get; set; }
    }
}
