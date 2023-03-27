using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    public class AuroraSetters
    {
        public string sourceUrl { get; set; }
        public List<string> keywords { get; set; }
        public List<Names> names { get; set; }
        public Tuple<string, string> names_format { get; set; } // says which types go in which order.
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
        public bool hasVerbalComponent { get; set; }
        public bool hasSomaticComponent { get; set; }
        public bool hasMaterialComponent { get; set; }
        public string materialComponent { get; set; }
        public bool isConcentration { get; set; }
        public bool isRitual { get; set; }
        public List<string> multiclass_proficiencies { get; set; }
    }

    public class Names
    {
        public string type { get; set; }
        public List<string> names { get; set; }
    }
}
