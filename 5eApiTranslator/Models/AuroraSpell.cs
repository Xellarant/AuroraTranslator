using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuroraTranslator.Models
{
    class AuroraSpell : Spell
    {
        public string source { get; set; }
        public string aurora_id { get; set; }
        public string source_file_path { get; set; }
        public bool compendium_display { get; set; }
        public string descriptionRawXml { get; set; }
        public AuroraSetters setters { get; set; }
    }
}

