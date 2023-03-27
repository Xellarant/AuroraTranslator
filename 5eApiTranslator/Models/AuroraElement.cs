using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    class AuroraElement
    {
        // attributes of the parent ("element") element.
        public string name { get; set; }
        public string type { get; set; }
        public string source { get; set; }
        public string id { get; set; }
        public string index { get; set; }

        // properties from child elements that might exist
        public Compendium compendium { get; set; } = new Compendium();
        public List<string> supports { get; set; } // list of stuff it supports
        public List<string> requirements { get; set; }
        public string description { get; set; }
        public AuroraSheet sheet { get; set; }
        public AuroraSetters setters { get; set; }
        public Spellcasting spellcasting { get; set; }
        public Multiclass multiclass { get; set; }
        public Rules rules { get; set; }
    }

    public class Multiclass
    {
        public string id { get; set; }
        public string prerequisite { get; set; } 
        public List<string> requirements { get; set; }
        public AuroraSetters setters { get; set; }
        public Rules rules { get; set; }
    }

    public class Rules
    {
        public List<Grant> grants { get; set; }
        public List<Select> selects { get; set; }
        public List<Stat> stats { get; set; }
    }

    public class Stat
    {
        public string name { get; set; }
        public string value { get; set; }
        public string bonus { get; set; }
        public List<string> equipped { get; set; }
        public int? level { get; set; }
        public List<string> requirements { get; set; }
        public bool inline { get; set; }
    }

    public class Select
    {
        public string type { get; set; }
        public string name { get; set; }
        public List<string> supports { get; set; }
        public int? level { get; set; }
        public List<string> requirements { get; set; }
        public int number { get; set; } // how many selections
        public string defaultChoice { get; set; } // selected by default
        public bool optional { get; set; }
    }

    public class Grant
    {
        public string type { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public int? level { get; set; }
        public List<string> requirements { get; set; }
    }

    public class Spellcasting
    {
        public string name { get; set; } // name of spellcasting class/archetype.
        public string ability { get; set; } // what ability score?
        public List<string> list { get; set; } // which spellcasting lists?
        public bool extend { get; set; } // are we extending an existing list?
        public List<string> extendList { get; set; } // other lists we're potentially including
    }

    public class Compendium
    {
        public bool display { get; set; } = true;
    }

    public class AuroraSheet
    {
        public bool display { get; set; } = true;
        public List<Description> description { get; set; }
        public string alt { get; set; } // alternative name to appear on the sheet
        public string action { get; set; } // what kind of action it takes
        public string usage { get; set; } // how often a thing can be used.
    }

    public class Description
    {
        public int? level { get; set; }
        public string text { get; set; }
    }
}
