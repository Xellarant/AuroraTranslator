using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    class AuroraSpell : Spell
    {
        public string source { get; set; }
        public string aurora_id { get; set; }
        public bool compendium_display { get; set; }
        public AuroraSetters setters { get; set; }
    }
}
